using DrvCtl.Drivers;
using DrvCtl.Offline;

namespace DrvCtl.Publication;

internal enum EvidenceStatus
{
    Solved,
    PrototypeSupported,
    Unsupported,
    OmittedByPolicy
}

internal sealed record DriverPublicationPlan(
    DriverStagingPlan SourceStagingPlan,
    OfflineApplyPlan Reflection,
    string WorkspaceRoot,
    string RepositoryIdentity,
    string RepositoryIdentitySource,
    string ComputedRepositoryIdentity,
    string PublishedInf,
    string PublishedCatalog,
    string AllocationRule,
    string DriverDatabaseHive,
    PublicationFileCopy[] FileOperations,
    PublicationRegistryValue[] RegistryValues,
    PublicationVolatileValue[] VolatileMetadata,
    PublicationOmittedOperation[] OmittedOperations,
    OemInfMapValidation[] OemInfMapValidation
);

internal sealed record PublicationFileCopy(
    string SourcePath,
    string DestinationRelativePath,
    string Purpose
);

internal sealed record PublicationRegistryValue(
    string Hive,
    string KeyPath,
    string Name,
    uint RegistryType,
    byte[] EncodedBytes,
    string Derivation,
    EvidenceStatus EvidenceStatus,
    string Confidence,
    string Volatility,
    bool RequiredForOfflineServicing,
    bool RequiredForPnPUnknown
);

internal sealed record PublicationVolatileValue(
    string Hive,
    string KeyPath,
    string Name,
    string Strategy
);

internal sealed record PublicationOmittedOperation(
    string Target,
    string Reason,
    EvidenceStatus EvidenceStatus,
    bool RequiredForOfflineServicing,
    bool RequiredForPnP
);

internal sealed record OemInfMapValidation(
    int[] OccupiedIndexes,
    string ExpectedHex,
    string ActualHex,
    bool Matches
);
