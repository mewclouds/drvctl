using System.Text.RegularExpressions;

namespace DrvCtl.Publication;

internal sealed partial class ResearchPublicationPolicy
{
    private static readonly OemInfMapValidation[] ExpectedMapCases =
    [
        ValidateMap([], ""),
        ValidateMap([0], "80"),
        ValidateMap([0, 1], "C0"),
        ValidateMap([0, 1, 2], "E0"),
        ValidateMap([0, 2], "A0")
    ];

    internal OemInfMapValidation[] OemInfMapValidation => ExpectedMapCases;

    internal string ValidateRepositoryIdentity(string packageDirectory)
    {
        string identity = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageDirectory)));
        if (!ExportedIdentityPattern().IsMatch(identity))
            throw new NotSupportedException($"Research publication requires an exported package directory identity; unsupported name: {identity}");
        return identity;
    }

    internal string SelectDriverDatabaseHive(string? driverClass) => driverClass?.ToUpperInvariant() switch
    {
        "SOFTWARECOMPONENT" => "DRIVERS",
        _ => "SYSTEM"
    };

    internal string AllocateOemInf(string treeRoot, string repositoryIdentity)
    {
        string? existing = FindExistingPublication(treeRoot, repositoryIdentity);
        if (existing is not null) return existing;
        HashSet<int> occupied = ReadPublishedIndexes(treeRoot);
        int index = 0;
        while (occupied.Contains(index)) index++;
        return $"oem{index}.inf";
    }

    internal byte[] EncodeOemInfMap(IEnumerable<int> occupiedIndexes)
    {
        int[] indexes = occupiedIndexes.Where(i => i >= 0).Distinct().Order().ToArray();
        if (indexes.Length == 0) return [];
        byte[] bytes = new byte[(indexes[^1] / 8) + 1];
        foreach (int index in indexes) bytes[index / 8] |= checked((byte)(0x80 >> (index % 8)));
        return bytes;
    }

    internal int ParseOemIndex(string publishedInf)
    {
        Match match = PublishedInfPattern().Match(publishedInf);
        return match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : throw new InvalidOperationException($"Invalid OEM INF identity: {publishedInf}");
    }

    private string? FindExistingPublication(string treeRoot, string repositoryIdentity)
    {
        foreach (string hiveName in new[] { "SYSTEM", "DRIVERS" })
        {
            string hivePath = Path.Combine(treeRoot, "Windows", "System32", "config", hiveName);
            if (!File.Exists(hivePath)) continue;
            using Registry.OfflineRegistryHive hive = Registry.OfflineRegistryHive.Open(hivePath);
            foreach (int index in ReadPublishedIndexes(treeRoot))
            {
                string candidate = $"oem{index}.inf";
                if (!hive.TryOpenKey($@"DriverDatabase\DriverInfFiles\{candidate}", out Registry.OfflineRegistryKey? key)) continue;
                using (key)
                {
                    Registry.OfflineRegistryValue value = key!.ReadValue(null);
                    if (value.Type == 7 && DecodeMultiString(value.Data).Contains(repositoryIdentity, StringComparer.OrdinalIgnoreCase)) return candidate;
                }
            }
        }
        return null;
    }

    private static HashSet<int> ReadPublishedIndexes(string treeRoot)
    {
        string infDirectory = Path.Combine(treeRoot, "Windows", "INF");
        if (!Directory.Exists(infDirectory)) return [];
        return Directory.EnumerateFiles(infDirectory, "oem*.inf", SearchOption.TopDirectoryOnly)
            .Select(path => PublishedInfPattern().Match(Path.GetFileName(path)))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet();
    }

    private static string[] DecodeMultiString(byte[] data)
    {
        string text = System.Text.Encoding.Unicode.GetString(data).TrimEnd('\0');
        return text.Length == 0 ? [] : text.Split('\0');
    }

    private static OemInfMapValidation ValidateMap(int[] indexes, string expected)
    {
        ResearchPublicationPolicy policy = new();
        string actual = Convert.ToHexString(policy.EncodeOemInfMap(indexes));
        return new OemInfMapValidation(indexes, expected, actual, actual.Equals(expected, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"^.+\.inf_amd64_[0-9a-f]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExportedIdentityPattern();

    [GeneratedRegex(@"^oem([0-9]+)\.inf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PublishedInfPattern();
}

