using DrvCtl.Analysis;
using DrvCtl.Images;

namespace DrvCtl.Publication;

internal sealed class DirectoryPublicationComparer
{
    internal PublicationSemanticComparison Compare(string referenceWim, int imageIndex, string prototypeRoot, string comparisonWorkspace, DriverPublicationPlan plan)
    {
        string referenceRoot = Path.Combine(comparisonWorkspace, "reference");
        Directory.CreateDirectory(referenceRoot);
        using (WimImage image = WimImage.Open(referenceWim)) image.ExtractPaths(imageIndex, referenceRoot, PublicationAnalysisScope.WimPaths);

        OfflineFileSnapshotEngine fileEngine = new();
        OfflineFileDelta[] fileDeltas = fileEngine.Compare(fileEngine.Capture(referenceRoot), fileEngine.Capture(prototypeRoot));
        List<PublicationComparisonItem> items = [];
        int exactFileCount = fileDeltas.Count(delta => delta.Change == OfflineFileChange.Unchanged);
        HashSet<string> plannedFiles = plan.FileOperations.Select(operation => operation.DestinationRelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (OfflineFileDelta delta in fileDeltas.Where(delta => delta.Change != OfflineFileChange.Unchanged))
        {
            if (delta.Path.Equals(@"Windows\INF\setupapi.offline.log", StringComparison.OrdinalIgnoreCase))
                items.Add(new(PublicationComparisonStatus.ExpectedPrototypeDifference, "File", delta.Path, "The prototype deliberately does not generate a servicing log."));
            else if (delta.Path is @"Windows\System32\config\SYSTEM" or @"Windows\System32\config\SOFTWARE" or @"Windows\System32\config\DRIVERS")
                items.Add(new(PublicationComparisonStatus.SemanticallyEquivalent, "File", delta.Path, "Raw hive layout is not a semantic equality requirement; logical values are compared separately."));
            else if (plannedFiles.Contains(delta.Path))
                items.Add(new(PublicationComparisonStatus.Contradiction, "File", delta.Path, $"Planned publication file differs: {delta.Change}."));
            else
                items.Add(new(PublicationComparisonStatus.Unsupported, "File", delta.Path, $"Reference-only or unexpected file state: {delta.Change}."));
        }

        OfflineRegistrySnapshotEngine registryEngine = new();
        Dictionary<string, PublicationRegistryValue> plannedValues = plan.RegistryValues.ToDictionary(value => RegistryIdentity(value.Hive, value.KeyPath, value.Name), StringComparer.OrdinalIgnoreCase);
        foreach ((string hive, string hiveRelativePath, string root) in PublicationAnalysisScope.RegistryRoots)
        {
            OfflineRegistrySnapshot expected = registryEngine.Capture(hive, Path.Combine(referenceRoot, hiveRelativePath), root);
            OfflineRegistrySnapshot actual = registryEngine.Capture(hive, Path.Combine(prototypeRoot, hiveRelativePath), root);
            foreach (OfflineRegistryDelta delta in registryEngine.Compare(expected, actual))
            {
                string identity = RegistryIdentity(delta.Hive, delta.KeyPath, delta.ValueName ?? string.Empty);
                if (plannedValues.TryGetValue(identity, out PublicationRegistryValue? planned))
                {
                    if (planned.Volatility == "Volatile" && delta.AfterValue?.Type == delta.BeforeValue?.Type && delta.AfterValue?.RawBytes.Length == 8)
                        items.Add(new(PublicationComparisonStatus.SemanticallyEquivalent, "Registry", identity, "Volatile FILETIME representation is structurally equivalent; exact time is intentionally ignored."));
                    else
                        items.Add(new(PublicationComparisonStatus.Contradiction, "Registry", identity, $"Independently planned value differs. Reference={delta.BeforeValue?.TypeName ?? "missing"}:{delta.BeforeValue?.RawHex ?? "missing"}; Prototype={delta.AfterValue?.TypeName ?? "missing"}:{delta.AfterValue?.RawHex ?? "missing"}."));
                }
                else if (IsExpectedUnsupported(delta))
                    items.Add(new(PublicationComparisonStatus.Unsupported, "Registry", identity, "Required servicing state has no independently justified prototype encoder."));
                else
                    items.Add(new(PublicationComparisonStatus.Unsupported, "Registry", identity, "Unplanned logical reference difference."));
            }
        }

        int exactRegistryValues = CountExactPlannedValues(referenceRoot, prototypeRoot, plan.RegistryValues);
        int semantic = items.Count(item => item.Status == PublicationComparisonStatus.SemanticallyEquivalent);
        int expectedDifferences = items.Count(item => item.Status == PublicationComparisonStatus.ExpectedPrototypeDifference);
        int unsupported = items.Count(item => item.Status == PublicationComparisonStatus.Unsupported);
        int contradictions = items.Count(item => item.Status == PublicationComparisonStatus.Contradiction);
        return new(exactFileCount, [.. items], exactFileCount + exactRegistryValues, semantic, expectedDifferences, unsupported, contradictions);
    }

    private static int CountExactPlannedValues(string referenceRoot, string prototypeRoot, PublicationRegistryValue[] values)
    {
        int count = 0;
        foreach (PublicationRegistryValue value in values)
        {
            string relative = Path.Combine("Windows", "System32", "config", value.Hive);
            using Registry.OfflineRegistryHive expectedHive = Registry.OfflineRegistryHive.Open(Path.Combine(referenceRoot, relative));
            using Registry.OfflineRegistryHive actualHive = Registry.OfflineRegistryHive.Open(Path.Combine(prototypeRoot, relative));
            Registry.OfflineRegistryKey? expectedKey = null;
            Registry.OfflineRegistryKey? actualKey = null;
            bool expectedExists = expectedHive.TryOpenKey(value.KeyPath, out expectedKey);
            bool actualExists = actualHive.TryOpenKey(value.KeyPath, out actualKey);
            if (!expectedExists || !actualExists) { expectedKey?.Dispose(); actualKey?.Dispose(); continue; }
            using (expectedKey) using (actualKey)
            {
                try
                {
                    Registry.OfflineRegistryValue expected = expectedKey!.ReadValue(value.Name.Length == 0 ? null : value.Name);
                    Registry.OfflineRegistryValue actual = actualKey!.ReadValue(value.Name.Length == 0 ? null : value.Name);
                    if (expected.Type == actual.Type && expected.Data.AsSpan().SequenceEqual(actual.Data)) count++;
                }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        return count;
    }

    private static bool IsExpectedUnsupported(OfflineRegistryDelta delta) =>
        delta.KeyPath.Contains(@"DriverDatabase\DeviceIds", StringComparison.OrdinalIgnoreCase) ||
        delta.KeyPath.Contains(@"DriverDatabase\DriverPackages", StringComparison.OrdinalIgnoreCase) ||
        delta.KeyPath.Contains(@"PnpLockdownFiles", StringComparison.OrdinalIgnoreCase) ||
        delta.KeyPath.Contains(@"ControlSet001\Services", StringComparison.OrdinalIgnoreCase);

    private static string RegistryIdentity(string hive, string key, string name) => $"{hive}\\{key}\\{name}";
}
