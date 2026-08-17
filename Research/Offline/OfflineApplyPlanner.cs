using DrvCtl.Drivers;

namespace DrvCtl.Offline;

internal sealed class OfflineApplyPlanner
{
    private static readonly string[] UnresolvedOperations =
    [
        "Exact FileRepository directory identity",
        "FileRepository package staging",
        "OEM INF allocation",
        "Windows\\INF publication",
        "Catalog database publication",
        "CatRoot publication",
        "SYSTEM vs DRIVERS DriverDatabase selection",
        "DriverDatabase representation",
        "PnpLockdownFiles ownership",
        "PnP resource ownership",
        "ImportDate",
        "UpdateDate",
        "SignerScore representation",
        "ManifestHash",
        "StatusFlags",
        "ConfigScope"
    ];

    internal OfflineApplyPlan Create(
        string packageDirectory,
        string workspace,
        string systemHive,
        string? softwareHive,
        string? driversHive)
    {
        DriverStagingPlan sourcePlan = new DriverStagingPlanner().Create(packageDirectory);
        List<OfflineHiveInput> hives = [new("SYSTEM", Path.GetFullPath(systemHive))];
        if (!string.IsNullOrWhiteSpace(softwareHive)) hives.Add(new("SOFTWARE", Path.GetFullPath(softwareHive)));
        if (!string.IsNullOrWhiteSpace(driversHive)) hives.Add(new("DRIVERS", Path.GetFullPath(driversHive)));

        Dictionary<string, StoreFilePlan> storeFiles = sourcePlan.StoreFiles.ToDictionary(file => file.FileName, StringComparer.OrdinalIgnoreCase);
        OfflineFileCopy[] fileOperations = sourcePlan.Reflection.Copies.Select(copy =>
        {
            if (!storeFiles.TryGetValue(copy.SourceFile, out StoreFilePlan? source))
            {
                throw new InvalidOperationException($"Reflected source '{copy.SourceFile}' is not a resolved package file.");
            }
            return new OfflineFileCopy(source.SourcePath, copy.DestinationPath);
        }).ToArray();

        List<OfflineRegistryKeyCreate> keys = [];
        List<OfflineRegistryValueSet> values = [];
        foreach (ReflectedService service in sourcePlan.Reflection.Services)
        {
            string keyPath = $"{OfflineControlSetSelector.PrototypeControlSet}\\Services\\{service.Name}";
            keys.Add(new OfflineRegistryKeyCreate("SYSTEM", keyPath));
            values.Add(new OfflineRegistryValueSet("SYSTEM", keyPath, "Type", OfflineRegistryValueType.Dword, service.Type, null));
            values.Add(new OfflineRegistryValueSet("SYSTEM", keyPath, "Start", OfflineRegistryValueType.Dword, service.Start, null));
            values.Add(new OfflineRegistryValueSet("SYSTEM", keyPath, "ErrorControl", OfflineRegistryValueType.Dword, service.ErrorControl, null));
            values.Add(new OfflineRegistryValueSet("SYSTEM", keyPath, "ImagePath", OfflineRegistryValueType.String, null, service.ImagePath));
        }

        OfflineSkippedOperation[] skipped = UnresolvedOperations
            .Select(name => new OfflineSkippedOperation(name, "Servicing semantics are unresolved; no operation was synthesized."))
            .Append(new OfflineSkippedOperation("Control-set discovery", OfflineControlSetSelector.Limitation))
            .ToArray();

        return new OfflineApplyPlan(
            sourcePlan,
            Path.GetFullPath(workspace),
            [.. hives],
            fileOperations,
            [.. keys],
            [.. values],
            skipped,
            OfflineControlSetSelector.PrototypeControlSet,
            OfflineControlSetSelector.Limitation);
    }
}
