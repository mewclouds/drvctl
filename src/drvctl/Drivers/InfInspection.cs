/*
 * Result shapes for InfInspector. InfIdentity is shared with the public
 * `list` command's identity lookup. The rest back the hidden `inspect-inf`
 * and `plan-driver` research commands.
 */

namespace DrvCtl.Drivers;

/// Full parse of a single INF file: identity, install sections, copy and
/// service directives, models, and strings.
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

/// Just the Version-section identity fields, the cheap subset InfInspector.InspectIdentity reads for `list`.
internal sealed record InfIdentity(
    string? Class,
    string? ClassGuid,
    string? Provider,
    string? DriverDate,
    string? DriverVersion,
    string? CatalogFile
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
