namespace DrvCtl.Analysis;

internal static class PublicationWorkspaceSafety
{
    internal static string ValidateNew(string requested, params string[] protectedPaths)
    {
        string workspace = Normalize(requested);
        string root = Path.GetPathRoot(workspace) ?? throw new InvalidOperationException($"Analysis workspace has no filesystem root: {workspace}");
        if (workspace.Equals(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Refusing to use a filesystem root as an analysis workspace: {workspace}");
        if (Directory.Exists(workspace) || File.Exists(workspace)) throw new InvalidOperationException($"Analysis workspace must not already exist: {workspace}");
        string windows = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        RejectOverlap(workspace, windows, "the live Windows directory");
        foreach (string protectedPath in protectedPaths)
        {
            string full = Normalize(protectedPath);
            string protectedDirectory = Directory.Exists(full) ? full : Path.GetDirectoryName(full) ?? full;
            RejectOverlap(workspace, protectedDirectory, protectedPath);
        }
        return workspace;
    }

    private static void RejectOverlap(string workspace, string protectedPath, string description)
    {
        if (SameOrBelow(workspace, protectedPath) || SameOrBelow(protectedPath, workspace))
            throw new InvalidOperationException($"Analysis workspace overlaps protected path '{description}': {workspace}");
    }

    private static bool SameOrBelow(string child, string parent) => child.Equals(parent, StringComparison.OrdinalIgnoreCase) || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string path)
    {
        string full = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(full);
        return root is not null && full.Equals(root, StringComparison.OrdinalIgnoreCase) ? root : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
