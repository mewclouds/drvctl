namespace DrvCtl.Drivers;

internal sealed record InfInspection(
    string Path,
    string? Class,
    string? ClassGuid,
    string? Provider,
    string? CatalogFile,
    string? DriverVersion,
    string? ModelsSection,
    string[] InstallSections,
    string[] CopyFilesDirectives,
    string[] AddServiceDirectives,
    string[] HardwareIds,
    InfCopyOperation[] CopyOperations,
    InfServiceOperation[] ServiceOperations,
    string? ExtensionId,
    bool HasAddSoftware,
    string[] SoftwareComponentIds,
    InfModelEntry[] Models,
    InfStringValue[] Strings,
    int? PnpLockdown,
    InfSignatureInfo? Signature
);

internal sealed record InfSignatureInfo(
    string? CatalogFile,
    string? SignerName,
    string? SignerVersion,
    uint SignerScore
);

internal sealed record InfModelEntry(
    string Description,
    string InstallSection,
    string[] Ids,
    string Manufacturer
);

internal sealed record InfStringValue(string Name, string Value);

internal sealed record InfCopyOperation(
    string InstallSection,
    string SourceFile,
    string DestinationFile,
    int DestinationDirectoryId,
    string? DestinationSubdirectory
);

internal sealed record InfServiceOperation(
    string InstallSection,
    string ServicesSection,
    string ConfigurationSection,
    string Name,
    int Flags,
    int ServiceType,
    int StartType,
    int ErrorControl,
    string ServiceBinary,
    string? DisplayName
);
