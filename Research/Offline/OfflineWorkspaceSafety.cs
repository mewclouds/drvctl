namespace DrvCtl.Offline;

internal static class OfflineWorkspaceSafety
{
    internal static string Validate(OfflineApplyPlan plan)
    {
        string workspace = Normalize(plan.Workspace);
        string root = Normalize(Path.GetPathRoot(workspace) ?? throw new InvalidOperationException("Workspace has no filesystem root."));
        if (PathsEqual(workspace, root)) throw new InvalidOperationException($"Refusing to use a filesystem root as a simulation workspace: {workspace}");
        string parent = Path.GetDirectoryName(workspace) ?? throw new InvalidOperationException($"Workspace has no parent directory: {workspace}");
        if (!Directory.Exists(parent)) throw new InvalidOperationException($"Simulation workspace parent must already exist so its resolved location can be validated: {parent}");
        string resolvedParent = ResolveExistingDirectory(parent);
        workspace = Normalize(Path.Combine(resolvedParent, Path.GetFileName(workspace)));

        string windows = ResolveExistingDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        RejectOverlap(workspace, windows, "the live Windows directory");
        RejectOverlap(workspace, ResolveExistingDirectory(plan.SourcePlan.Package.Directory), "the package directory");
        foreach (OfflineHiveInput hive in plan.HiveInputs)
        {
            if (!File.Exists(hive.SourcePath)) throw new FileNotFoundException($"Offline {hive.Name} hive was not found.", hive.SourcePath);
            string source = Normalize(hive.SourcePath);
            string sourceDirectory = ResolveExistingDirectory(Path.GetDirectoryName(source) ?? throw new InvalidOperationException($"Hive path has no parent: {source}"));
            string resolvedSource = Path.Combine(sourceDirectory, Path.GetFileName(source));
            if (IsSameOrBelow(resolvedSource, windows)) throw new InvalidOperationException($"Refusing to use a live Windows hive as simulation input: {source}");
            RejectOverlap(workspace, sourceDirectory, $"the {hive.Name} source hive directory");
        }
        if (Directory.Exists(workspace) || File.Exists(workspace)) throw new InvalidOperationException($"Simulation workspace must not already exist: {workspace}");
        return workspace;
    }

    internal static string ResolveOutputPath(string workspace, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new InvalidOperationException($"Offline output path must be relative: {relativePath}");
        string output = Normalize(Path.Combine(workspace, relativePath));
        if (!IsSameOrBelow(output, workspace)) throw new InvalidOperationException($"Offline output escapes the workspace: {relativePath}");
        return output;
    }

    private static string ResolveExistingDirectory(string path)
    {
        string fullPath = Normalize(path);
        string root = Path.GetPathRoot(fullPath) ?? throw new InvalidOperationException($"Path has no filesystem root: {path}");
        string current = root;
        foreach (string segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            DirectoryInfo directory = new(Path.Combine(current, segment));
            FileSystemInfo? resolved = directory.LinkTarget is null ? null : directory.ResolveLinkTarget(true);
            current = resolved?.FullName ?? directory.FullName;
        }
        return Normalize(current);
    }

    private static void RejectOverlap(string workspace, string protectedPath, string description)
    {
        if (IsSameOrBelow(workspace, protectedPath) || IsSameOrBelow(protectedPath, workspace))
            throw new InvalidOperationException($"Refusing a simulation workspace that overlaps {description}: {workspace}");
    }

    private static bool IsSameOrBelow(string child, string parent)
    {
        child = Normalize(child);
        parent = Normalize(parent);
        return PathsEqual(child, parent) || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        return root is not null && fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            ? root
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
