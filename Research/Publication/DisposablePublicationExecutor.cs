using System.Security.Cryptography;
using DrvCtl.Copy;
using DrvCtl.Registry;

namespace DrvCtl.Publication;

internal sealed class DisposablePublicationExecutor(ICopyEngine copyEngine)
{
    internal (PublicationAppliedFile[] Files, PublicationAppliedRegistryValue[] Registry) Execute(DriverPublicationPlan plan)
    {
        foreach (PublicationRegistryValue val in plan.RegistryValues)
        {
            if (val.EvidenceStatus == EvidenceStatus.Unsupported)
                throw new InvalidOperationException($"Executor safety check failed: unsupported registry value '{val.Hive}\\{val.KeyPath}\\{val.Name}' cannot be executed.");
        }

        List<PublicationAppliedFile> files = [];
        foreach (PublicationFileCopy operation in plan.FileOperations)
        {
            string destination = ResolveInside(plan.WorkspaceRoot, operation.DestinationRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string sourceHash = Hash(operation.SourcePath);
            copyEngine.Copy(operation.SourcePath, destination);
            string destinationHash = Hash(destination);
            files.Add(new(operation.SourcePath, destination, sourceHash, destinationHash, new FileInfo(destination).Length, sourceHash.Equals(destinationHash, StringComparison.Ordinal)));
        }

        List<PublicationAppliedRegistryValue> applied = [];
        foreach (IGrouping<string, PublicationRegistryValue> hiveValues in plan.RegistryValues.GroupBy(value => value.Hive, StringComparer.OrdinalIgnoreCase))
        {
            string hivePath = Path.Combine(plan.WorkspaceRoot, "Windows", "System32", "config", hiveValues.Key);
            string savedPath = hivePath + ".drvctl-new";
            using (OfflineRegistryHive hive = OfflineRegistryHive.Open(hivePath))
            {
                foreach (IGrouping<string, PublicationRegistryValue> keyValues in hiveValues.GroupBy(value => value.KeyPath, StringComparer.OrdinalIgnoreCase))
                {
                    OfflineRegistryKey key;
                    try { key = EnsureKey(hive, keyValues.Key); }
                    catch (Exception error) { throw new InvalidOperationException($"Could not create publication registry key '{hiveValues.Key}\\{keyValues.Key}'.", error); }
                    using (key)
                        foreach (PublicationRegistryValue value in keyValues)
                        {
                            key.SetValue(value.Name.Length == 0 ? null : value.Name, value.RegistryType, value.EncodedBytes);
                            applied.Add(new(value.Hive, value.KeyPath, value.Name, TypeName(value.RegistryType), Convert.ToHexString(value.EncodedBytes), value.Derivation, value.EvidenceStatus));
                        }
                }
                hive.Save(savedPath);
            }
            File.Move(savedPath, hivePath, true);
        }
        return ([.. files], [.. applied]);
    }

    private static OfflineRegistryKey EnsureKey(OfflineRegistryHive hive, string keyPath)
    {
        string current = string.Empty;
        foreach (string segment in keyPath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 ? segment : current + "\\" + segment;
            if (hive.TryOpenKey(current, out OfflineRegistryKey? existing)) existing!.Dispose();
            else using (hive.CreateKey(current)) { }
        }
        return hive.OpenKey(keyPath);
    }

    private static string ResolveInside(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidOperationException($"Publication output must be relative: {relative}");
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Publication output escapes the disposable tree: {relative}");
        return full;
    }

    internal static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string TypeName(uint type) => type switch { 1 => "REG_SZ", 2 => "REG_EXPAND_SZ", 3 => "REG_BINARY", 4 => "REG_DWORD", 7 => "REG_MULTI_SZ", 11 => "REG_QWORD", _ => $"REG_TYPE_{type}" };
}
