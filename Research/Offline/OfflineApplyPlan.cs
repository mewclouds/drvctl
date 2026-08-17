using DrvCtl.Drivers;

namespace DrvCtl.Offline;

internal sealed record OfflineApplyPlan(
    DriverStagingPlan SourcePlan,
    string Workspace,
    OfflineHiveInput[] HiveInputs,
    OfflineFileCopy[] FileOperations,
    OfflineRegistryKeyCreate[] RegistryKeys,
    OfflineRegistryValueSet[] RegistryValues,
    OfflineSkippedOperation[] SkippedOperations,
    string ControlSet,
    string ControlSetLimitation
);

internal sealed record OfflineHiveInput(string Name, string SourcePath);

internal sealed record OfflineFileCopy(
    string SourcePath,
    string DestinationRelativePath
);

internal sealed record OfflineRegistryKeyCreate(string Hive, string KeyPath);

internal sealed record OfflineRegistryValueSet(
    string Hive,
    string KeyPath,
    string Name,
    OfflineRegistryValueType Type,
    int? DwordValue,
    string? StringValue
);

internal enum OfflineRegistryValueType
{
    Dword,
    String
}

internal sealed record OfflineSkippedOperation(string Name, string Reason);
