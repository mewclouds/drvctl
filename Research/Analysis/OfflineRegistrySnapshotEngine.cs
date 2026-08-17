using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using DrvCtl.Registry;

namespace DrvCtl.Analysis;

internal sealed class OfflineRegistrySnapshotEngine
{
    internal OfflineRegistrySnapshot Capture(string hiveName, string hivePath, string rootPath)
    {
        using OfflineRegistryHive hive = OfflineRegistryHive.Open(hivePath);
        if (!hive.TryOpenKey(rootPath, out OfflineRegistryKey? root)) return new OfflineRegistrySnapshot(hiveName, rootPath, false, []);
        using (root)
        {
            List<OfflineRegistryKeyState> keys = [];
            CaptureKey(root!, rootPath, keys);
            return new OfflineRegistrySnapshot(hiveName, rootPath, true, [.. keys.OrderBy(key => key.Path, StringComparer.OrdinalIgnoreCase).ThenBy(key => key.Path, StringComparer.Ordinal)]);
        }
    }

    internal OfflineRegistryDelta[] Compare(OfflineRegistrySnapshot before, OfflineRegistrySnapshot after)
    {
        if (!before.Hive.Equals(after.Hive, StringComparison.OrdinalIgnoreCase) || !before.RootPath.Equals(after.RootPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Registry snapshots must describe the same hive and root.");
        Dictionary<string, OfflineRegistryKeyState> beforeKeys = before.Keys.ToDictionary(key => key.Path, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, OfflineRegistryKeyState> afterKeys = after.Keys.ToDictionary(key => key.Path, StringComparer.OrdinalIgnoreCase);
        List<OfflineRegistryDelta> deltas = [];
        foreach (string keyPath in beforeKeys.Keys.Union(afterKeys.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ThenBy(path => path, StringComparer.Ordinal))
        {
            OfflineRegistryKeyState? beforeKey = beforeKeys.GetValueOrDefault(keyPath);
            OfflineRegistryKeyState? afterKey = afterKeys.GetValueOrDefault(keyPath);
            if (beforeKey is null) deltas.Add(new OfflineRegistryDelta(OfflineRegistryChange.KeyAdded, before.Hive, keyPath, null, null, afterKey, null, null));
            else if (afterKey is null) deltas.Add(new OfflineRegistryDelta(OfflineRegistryChange.KeyRemoved, before.Hive, keyPath, null, beforeKey, null, null, null));
            CompareValues(before.Hive, keyPath, beforeKey?.Values ?? [], afterKey?.Values ?? [], deltas);
        }
        return [.. deltas];
    }

    private static void CaptureKey(OfflineRegistryKey key, string path, List<OfflineRegistryKeyState> keys)
    {
        OfflineRegistryValueState[] values = key.EnumerateValues()
            .Select(value => CreateValue(path, value))
            .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Name, StringComparer.Ordinal)
            .ToArray();
        keys.Add(new OfflineRegistryKeyState(path, values));
        foreach (string childName in key.EnumerateSubKeyNames().Order(StringComparer.OrdinalIgnoreCase).ThenBy(name => name, StringComparer.Ordinal))
        {
            using OfflineRegistryKey child = key.OpenKey(childName);
            CaptureKey(child, path + "\\" + childName, keys);
        }
    }

    private static OfflineRegistryValueState CreateValue(string keyPath, OfflineRegistryValue value)
    {
        (string? decoded, string[]? decodedStrings) = Decode(value.Type, value.Data);
        return new OfflineRegistryValueState(keyPath, value.Name, value.Type, TypeName(value.Type), value.Data, Convert.ToHexString(value.Data), decoded, decodedStrings);
    }

    private static void CompareValues(string hive, string keyPath, OfflineRegistryValueState[] before, OfflineRegistryValueState[] after, List<OfflineRegistryDelta> deltas)
    {
        Dictionary<string, OfflineRegistryValueState> beforeValues = before.ToDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, OfflineRegistryValueState> afterValues = after.ToDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
        foreach (string name in beforeValues.Keys.Union(afterValues.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ThenBy(value => value, StringComparer.Ordinal))
        {
            OfflineRegistryValueState? beforeValue = beforeValues.GetValueOrDefault(name);
            OfflineRegistryValueState? afterValue = afterValues.GetValueOrDefault(name);
            OfflineRegistryChange? change = beforeValue is null
                ? OfflineRegistryChange.ValueAdded
                : afterValue is null
                    ? OfflineRegistryChange.ValueRemoved
                    : beforeValue.Type != afterValue.Type || !beforeValue.RawBytes.AsSpan().SequenceEqual(afterValue.RawBytes)
                        ? OfflineRegistryChange.ValueChanged
                        : null;
            if (change.HasValue) deltas.Add(new OfflineRegistryDelta(change.Value, hive, keyPath, name, null, null, beforeValue, afterValue));
        }
    }

    private static (string? Decoded, string[]? DecodedStrings) Decode(uint type, byte[] data)
    {
        if (type is 1 or 2)
        {
            if (data.Length % 2 != 0) return (null, null);
            string text = Encoding.Unicode.GetString(data);
            if (text.EndsWith('\0')) text = text[..^1];
            return text.Contains('\0') ? (null, null) : (text, null);
        }
        if (type == 4 && data.Length == 4) return (BinaryPrimitives.ReadUInt32LittleEndian(data).ToString(CultureInfo.InvariantCulture), null);
        if (type == 7)
        {
            if (data.Length % 2 != 0) return (null, null);
            string text = Encoding.Unicode.GetString(data);
            while (text.EndsWith('\0')) text = text[..^1];
            return (null, text.Length == 0 ? [] : text.Split('\0', StringSplitOptions.None));
        }
        if (type == 11 && data.Length == 8) return (BinaryPrimitives.ReadUInt64LittleEndian(data).ToString(CultureInfo.InvariantCulture), null);
        return (null, null);
    }

    private static string TypeName(uint type) => type switch
    {
        0 => "REG_NONE",
        1 => "REG_SZ",
        2 => "REG_EXPAND_SZ",
        3 => "REG_BINARY",
        4 => "REG_DWORD",
        7 => "REG_MULTI_SZ",
        11 => "REG_QWORD",
        _ => $"REG_TYPE_{type}"
    };
}
