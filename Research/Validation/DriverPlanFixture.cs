using DrvCtl.Drivers;

namespace DrvCtl.Validation;

internal sealed record DriverPlanFixture(
    string Name,
    string PackageDirectoryName,
    string[] StoreFiles,
    string[] DeviceIds,
    ExpectedCopy[] Copies,
    ExpectedService[] Services,
    ObservedServicingFact[] Observations
);

internal sealed record ExpectedCopy(string SourceFile, string DestinationPath);

internal sealed record ExpectedService(
    string Name,
    int Type,
    int Start,
    int ErrorControl,
    string ImagePath
);

internal sealed record ObservedServicingFact(
    string Name,
    ObservedServicingField Field,
    string? Value
);

internal enum ObservedServicingField
{
    PublishedInfIdentity,
    CatalogPublication,
    FileRepositoryIdentity,
    DriverDatabaseHive,
    DriverDatabaseRepresentation,
    OwnershipMetadata,
    SoftwareRegistryDelta,
    ReflectedFileByteIdentity
}

internal enum SemanticValidationStatus
{
    DerivedCorrectly,
    ObservedButUnresolved,
    Contradiction
}

internal sealed record SemanticValidationResult(
    string Name,
    SemanticValidationStatus Status,
    string Detail
);

internal sealed record DriverPlanValidation(
    DriverPlanFixture Fixture,
    DriverStagingPlan Plan,
    SemanticValidationResult[] Results
)
{
    internal bool HasContradictions => Results.Any(result => result.Status == SemanticValidationStatus.Contradiction);
}
