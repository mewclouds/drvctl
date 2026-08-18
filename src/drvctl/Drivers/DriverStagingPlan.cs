/*
 * Result shapes for DriverStagingPlanner, backing the hidden `plan-driver`
 * research command. Represents what drvctl can currently determine about
 * how a driver package would install, split explicitly from what remains
 * UnresolvedOperations so the gap is visible rather than guessed at.
 */

namespace DrvCtl.Drivers;

/// A driver package's install plan as far as drvctl can currently determine
/// it, plus the list of operations that remain unresolved research questions.
internal sealed record DriverStagingPlan(
    DriverPackagePlan Package,
    StoreFilePlan[] StoreFiles,
    PublishedArtifactPlan PublishedInf,
    PublishedArtifactPlan PublishedCatalog,
    string[] DeviceIds,
    DriverDatabasePlan DriverDatabase,
    DriverReflectionPlan Reflection,
    string[] UnresolvedOperations
);

internal sealed record DriverPackagePlan(
    string Directory,
    string Inf,
    string? Class,
    string? ClassGuid,
    string? Provider,
    string? DriverVersion,
    string? Catalog,
    string Architecture,
    string[] InstallSections
);

internal sealed record StoreFilePlan(string FileName, string SourcePath);

internal sealed record PublishedArtifactPlan(
    string SourceFile,
    string? PublishedIdentity,
    string Status
);

internal sealed record DriverDatabasePlan(
    string? TargetHive,
    string? Representation,
    string Status
);

internal sealed record DriverReflectionPlan(
    ReflectedFileCopy[] Copies,
    ReflectedService[] Services
);

internal sealed record ReflectedFileCopy(
    string InstallSection,
    string SourceFile,
    string DestinationPath
);

internal sealed record ReflectedService(
    string InstallSection,
    string ServicesSection,
    string ConfigurationSection,
    string Name,
    int Type,
    int Start,
    int ErrorControl,
    string ImagePath
);
