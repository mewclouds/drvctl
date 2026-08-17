using DrvCtl.Drivers;

namespace DrvCtl.Analysis;

internal sealed record PublicationAnalysisReport(
    string BaselineWim,
    string ServicedWim,
    int ImageIndex,
    string Workspace,
    PublicationSourcePackage SourcePackage,
    OfflineFileDelta[] FileDeltas,
    OfflineRegistryDelta[] RegistryDeltas,
    DriverPublicationObservation Observation,
    ServiceFieldComparison[] ServiceComparisons,
    string[] UnresolvedObservations,
    string[] Contradictions
);

internal sealed record PublicationSourcePackage(
    string Directory,
    string RepositoryIdentity,
    string Inf,
    string InfSha256,
    string? Catalog,
    string? CatalogSha256,
    string? Class,
    string? ClassGuid,
    string? Provider,
    string? DriverVersion,
    string[] InstallSections,
    string[] DeviceIds,
    bool HasDirId13Files,
    bool HasAddService,
    string? ExtensionId,
    bool HasAddSoftware,
    string[] SoftwareComponentIds,
    string[] ReflectedFiles,
    string[] ReflectedServices
);

internal sealed record DriverPublicationObservation(
    string KnownSourceRepositoryIdentity,
    string[] ObservedServicedRepositoryIdentities,
    bool SourceIdentityMatchesObserved,
    string ComputedRepositoryIdentity,
    string[] BaselinePublishedInfs,
    string[] ServicedPublishedInfs,
    string[] NewPublishedInfs,
    PublishedFileObservation[] InfPublications,
    PublishedFileObservation[] CatalogPublications,
    string[] ModifiedExistingCatRootFiles,
    string[] SelectedDriverDatabaseHives,
    OfflineRegistryDelta[] DeviceIdMappings,
    OfflineRegistryDelta[] DriverInfFileRecords,
    OfflineRegistryDelta[] DriverPackageRecords,
    OfflineRegistryDelta[] OwnershipRecords,
    OfflineRegistryDelta[] ServiceRecords,
    OfflineRegistryDelta[] OtherRegistryChanges,
    OfflineFileDelta[] OtherFileChanges
);

internal sealed record PublishedFileObservation(
    string PublishedPath,
    string PublishedSha256,
    string SourcePath,
    string SourceSha256,
    bool SourceHashMatches,
    string? RepositoryPath,
    string? RepositorySha256,
    bool? RepositoryHashMatches
);

internal enum ServiceComparisonStatus
{
    DerivedCorrectly,
    ObservedExtraServicingMetadata,
    Contradiction
}

internal sealed record ServiceFieldComparison(
    string ServiceName,
    string ValueName,
    ServiceComparisonStatus Status,
    string? PlannedValue,
    OfflineRegistryValueState? ObservedValue,
    string Detail
);
