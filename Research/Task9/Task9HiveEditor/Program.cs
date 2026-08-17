using System.Text.Json;
using DrvCtl.Registry;

if (args.Length is < 8 or > 9)
{
    Console.Error.WriteLine("Usage: Task9HiveEditor <input> <output> <manifest> <experiment> <source-wim-sha256> <hive> <operation> <key> [value-or-hex]");
    return 2;
}

string input = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
string manifestPath = Path.GetFullPath(args[2]);
string experiment = args[3];
string sourceWimHash = args[4];
string hiveName = args[5];
string operation = args[6];
string keyPath = args[7];
string? argument = args.Length == 9 ? args[8] : null;

if (File.Exists(output)) throw new IOException($"Output hive already exists: {output}");

RegistryTreeState? removedTree = null;
RegistryValueState? before = null;
RegistryValueState? after = null;
string? valueName = null;

using (OfflineRegistryHive hive = OfflineRegistryHive.Open(input))
{
    switch (operation)
    {
        case "delete-value":
            valueName = argument ?? throw new ArgumentException("delete-value requires a value name.");
            if (valueName == "@default") valueName = string.Empty;
            using (OfflineRegistryKey key = hive.OpenKey(keyPath))
            {
                before = RegistryValueState.From(key.ReadValue(valueName));
                key.DeleteValue(valueName);
            }
            break;

        case "set-binary-zero":
            valueName = argument ?? throw new ArgumentException("set-binary-zero requires a value name.");
            using (OfflineRegistryKey key = hive.OpenKey(keyPath))
            {
                before = RegistryValueState.From(key.ReadValue(valueName));
                key.SetValue(valueName, 3, new byte[before.RawBytes.Length / 2]);
                after = RegistryValueState.From(key.ReadValue(valueName));
            }
            break;

        case "zero-tail-8":
            valueName = argument ?? throw new ArgumentException("zero-tail-8 requires a value name.");
            using (OfflineRegistryKey key = hive.OpenKey(keyPath))
            {
                OfflineRegistryValue value = key.ReadValue(valueName);
                before = RegistryValueState.From(value);
                if (value.Type != 3 || value.Data.Length < 8) throw new InvalidDataException("zero-tail-8 requires a REG_BINARY value at least eight bytes long.");
                byte[] replacement = [.. value.Data];
                replacement.AsSpan(replacement.Length - 8).Clear();
                if (replacement.AsSpan().SequenceEqual(value.Data)) throw new InvalidOperationException("The final eight bytes are already zero; refusing a no-op mutation.");
                key.SetValue(valueName, value.Type, replacement);
                after = RegistryValueState.From(key.ReadValue(valueName));
            }
            break;

        case "delete-tree":
            (string parentPath, string childName) = SplitParent(keyPath);
            using (OfflineRegistryKey parent = hive.OpenKey(parentPath))
            using (OfflineRegistryKey child = parent.OpenKey(childName)) removedTree = Snapshot(childName, child);
            using (OfflineRegistryKey parent = hive.OpenKey(parentPath)) parent.DeleteSubKeyTree(childName);
            break;

        default:
            throw new ArgumentException($"Unsupported mutation operation: {operation}");
    }

    hive.Save(output);
}

MutationManifest manifest = new(
    experiment,
    sourceWimHash,
    hiveName,
    keyPath,
    valueName,
    operation,
    before?.Type,
    before?.RawBytes,
    after?.Type,
    after?.RawBytes,
    true,
    removedTree);

Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
return 0;

static (string Parent, string Child) SplitParent(string path)
{
    int separator = path.LastIndexOf('\\');
    if (separator <= 0 || separator == path.Length - 1) throw new ArgumentException($"A non-root key path is required: {path}");
    return (path[..separator], path[(separator + 1)..]);
}

static RegistryTreeState Snapshot(string name, OfflineRegistryKey key)
{
    RegistryValueState[] values = key.EnumerateValues()
        .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
        .Select(RegistryValueState.From)
        .ToArray();
    List<RegistryTreeState> children = [];
    foreach (string childName in key.EnumerateSubKeyNames().OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
    {
        using OfflineRegistryKey child = key.OpenKey(childName);
        children.Add(Snapshot(childName, child));
    }
    return new RegistryTreeState(name, values, [.. children]);
}

internal sealed record RegistryValueState(string Name, uint Type, string RawBytes)
{
    internal static RegistryValueState From(OfflineRegistryValue value) => new(value.Name, value.Type, Convert.ToHexString(value.Data));
}

internal sealed record RegistryTreeState(string Name, RegistryValueState[] Values, RegistryTreeState[] SubKeys);

internal sealed record MutationManifest(
    string ExperimentId,
    string SourceWimHash,
    string TargetHive,
    string RegistryPath,
    string? ValueName,
    string Operation,
    uint? BeforeType,
    string? BeforeRawBytes,
    uint? AfterType,
    string? AfterRawBytes,
    bool ExpectedSingleMutation,
    RegistryTreeState? RemovedSubtree);
