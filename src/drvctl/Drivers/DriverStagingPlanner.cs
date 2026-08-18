/*
 * Backs the hidden `plan-driver` research command. Derives what an INF's
 * copy and service directives would resolve to on a running system, without
 * actually installing anything. The UnresolvedOperations list below is the
 * honest inventory of what publication behavior this planner does not model.
 */

namespace DrvCtl.Drivers;

/// Builds a DriverStagingPlan from a single driver package directory.
internal sealed class DriverStagingPlanner
{
    private const int DriversDirectoryId = 12;
    private const int DriverStoreDirectoryId = 13;
    private const string Architecture = "AMD64";

    private static readonly string[] UnresolvedOperations =
    [
        "Exact FileRepository directory identity",
        "OEM INF allocation",
        "Catalog database publication",
        "SYSTEM vs DRIVERS DriverDatabase selection",
        "DriverDatabase binary/value encoding",
        "PnpLockdownFiles ownership encoding",
        "ImportDate",
        "UpdateDate",
        "SignerScore representation",
        "ManifestHash",
        "StatusFlags",
        "ConfigScope"
    ];

    /// <exception cref="DirectoryNotFoundException">The package directory does not exist.</exception>
    /// <exception cref="InvalidOperationException">The directory does not contain exactly one INF.</exception>
    internal DriverStagingPlan Create(string packageDirectory)
    {
        string directory = Path.GetFullPath(packageDirectory);
        if (!System.IO.Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Driver package directory was not found: {directory}");
        }

        string[] infFiles = System.IO.Directory.GetFiles(directory, "*.inf", SearchOption.TopDirectoryOnly);
        if (infFiles.Length != 1)
        {
            throw new InvalidOperationException($"Expected exactly one INF in '{directory}', but found {infFiles.Length}.");
        }

        InfInspection inspection = new InfInspector().Inspect(infFiles[0]);
        string[] actualFiles = System.IO.Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
        StoreFilePlan[] storeFiles = actualFiles
            .Select(file => new StoreFilePlan(Path.GetFileName(file), file))
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Dictionary<string, StoreFilePlan> fileMap = storeFiles.ToDictionary(f => f.FileName, StringComparer.OrdinalIgnoreCase);

        ReflectedFileCopy[] copies = inspection.CopyOperations
            .Where(copy => copy.DestinationDirectoryId != DriverStoreDirectoryId && fileMap.ContainsKey(copy.SourceFile))
            .Select(copy => new ReflectedFileCopy(copy.InstallSection, copy.SourceFile, ResolveDestination(copy)))
            .ToArray();

        ReflectedService[] services = inspection.ServiceOperations
            .Select(service => new ReflectedService(
                service.InstallSection,
                service.ServicesSection,
                service.ConfigurationSection,
                service.Name,
                service.ServiceType,
                service.StartType,
                service.ErrorControl,
                ResolveServiceBinary(service.ServiceBinary)))
            .ToArray();

        string infName = Path.GetFileName(inspection.Path);
        string catalogName = inspection.CatalogFile ?? "(not declared)";
        return new DriverStagingPlan(
            new DriverPackagePlan(directory, infName, inspection.Class, inspection.ClassGuid, inspection.Provider, inspection.DriverVersion, inspection.CatalogFile, Architecture, inspection.InstallSections),
            storeFiles,
            new PublishedArtifactPlan(infName, null, "Unresolved: OEM INF allocation is not implemented"),
            new PublishedArtifactPlan(catalogName, null, "Unresolved: catalog database publication is not implemented"),
            inspection.HardwareIds,
            new DriverDatabasePlan(null, null, "Unresolved: target hive and representation are not implemented"),
            new DriverReflectionPlan(copies, services),
            [.. UnresolvedOperations]);
    }

    /// Maps a DestinationDirs directory ID to the well-known Windows path it
    /// represents. Only the IDs actually seen in driver INFs are handled
    /// (10/11/12/13, plus the legacy 16425 alias for the drivers directory).
    private static string ResolveDestination(InfCopyOperation copy)
    {
        string root = copy.DestinationDirectoryId switch
        {
            10 => @"Windows",
            11 => @"Windows\System32",
            12 or 16425 => @"Windows\System32\drivers",
            13 => @"Windows\System32\DriverStore",
            _ => @"Windows\System32"
        };
        return string.IsNullOrWhiteSpace(copy.DestinationSubdirectory)
            ? Path.Combine(root, copy.DestinationFile)
            : Path.Combine(root, copy.DestinationSubdirectory, copy.DestinationFile);
    }

    /// Normalizes a ServiceBinary field (which may use %11%/%12% path
    /// substitution, an already-qualified \SystemRoot path, or a bare
    /// filename) to the \SystemRoot-qualified form the service control
    /// manager would actually store.
    private static string ResolveServiceBinary(string value)
    {
        if (value.StartsWith("%12%", StringComparison.OrdinalIgnoreCase))
            return @"\SystemRoot\System32\drivers\" + value[4..].TrimStart('\\', '/');
        if (value.StartsWith("%11%", StringComparison.OrdinalIgnoreCase))
            return @"\SystemRoot\System32\" + value[4..].TrimStart('\\', '/');
        if (value.StartsWith(@"\SystemRoot", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
            return value;
        string clean = value.TrimStart('.', '\\', '/');
        if (clean.EndsWith(".sys", StringComparison.OrdinalIgnoreCase))
            return @"\SystemRoot\System32\drivers\" + Path.GetFileName(clean);
        return @"\SystemRoot\System32\" + Path.GetFileName(clean);
    }
}
