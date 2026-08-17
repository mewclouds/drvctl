using System.Security.Cryptography;

namespace DrvCtl.Analysis;

internal sealed class OfflineFileSnapshotEngine
{
    internal OfflineFileSnapshot Capture(string root)
    {
        string fullRoot = Path.GetFullPath(root);
        OfflineFileState[] files = Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
            .Select(path => new OfflineFileState(Normalize(Path.GetRelativePath(fullRoot, path)), new FileInfo(path).Length, Hash(path)))
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
        return new OfflineFileSnapshot(fullRoot, files);
    }

    internal OfflineFileDelta[] Compare(OfflineFileSnapshot before, OfflineFileSnapshot after)
    {
        Dictionary<string, OfflineFileState> beforeFiles = before.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, OfflineFileState> afterFiles = after.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        return beforeFiles.Keys.Union(afterFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.Ordinal)
            .Select(path => CompareFile(path, beforeFiles.GetValueOrDefault(path), afterFiles.GetValueOrDefault(path)))
            .ToArray();
    }

    internal static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static OfflineFileDelta CompareFile(string path, OfflineFileState? before, OfflineFileState? after)
    {
        OfflineFileChange change = before is null
            ? OfflineFileChange.Added
            : after is null
                ? OfflineFileChange.Removed
                : before.Size == after.Size && before.Sha256.Equals(after.Sha256, StringComparison.Ordinal)
                    ? OfflineFileChange.Unchanged
                    : OfflineFileChange.Modified;
        return new OfflineFileDelta(change, path, before, after);
    }

    internal static string Normalize(string path) => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
}
