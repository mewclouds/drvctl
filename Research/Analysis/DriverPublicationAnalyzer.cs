using System.Text.Json;
using DrvCtl.Drivers;
using DrvCtl.Images;

namespace DrvCtl.Analysis;

internal sealed class DriverPublicationAnalyzer
{
    private static readonly string[] Unresolved =
    [
        "FileRepository suffix generation",
        "OEM INF allocation rule",
        "Catalog publication algorithm",
        "SYSTEM vs DRIVERS DriverDatabase selection rule",
        "DriverDatabase encoding and write semantics",
        "PnP ownership write semantics"
    ];

    internal PublicationAnalysisReport Analyze(string baselineWim, string servicedWim, string packageDirectory, int imageIndex, string requestedWorkspace)
    {
        string baseline = Path.GetFullPath(baselineWim);
        string serviced = Path.GetFullPath(servicedWim);
        string package = Path.GetFullPath(packageDirectory);
        if (!File.Exists(baseline)) throw new FileNotFoundException("Baseline WIM was not found.", baseline);
        if (!File.Exists(serviced)) throw new FileNotFoundException("Serviced WIM was not found.", serviced);
        string workspace = PublicationWorkspaceSafety.ValidateNew(requestedWorkspace, baseline, serviced, package);
        string baselineRoot = Path.Combine(workspace, "baseline");
        string servicedRoot = Path.Combine(workspace, "serviced");
        Directory.CreateDirectory(baselineRoot);
        Directory.CreateDirectory(servicedRoot);

        using (WimImage image = WimImage.Open(baseline)) image.ExtractPaths(imageIndex, baselineRoot, PublicationAnalysisScope.WimPaths);
        using (WimImage image = WimImage.Open(serviced)) image.ExtractPaths(imageIndex, servicedRoot, PublicationAnalysisScope.WimPaths);

        OfflineFileSnapshotEngine fileEngine = new();
        OfflineFileSnapshot baselineFiles = fileEngine.Capture(baselineRoot);
        OfflineFileSnapshot servicedFiles = fileEngine.Capture(servicedRoot);
        OfflineFileDelta[] fileDeltas = fileEngine.Compare(baselineFiles, servicedFiles);

        OfflineRegistrySnapshotEngine registryEngine = new();
        List<OfflineRegistrySnapshot> baselineRegistry = [];
        List<OfflineRegistrySnapshot> servicedRegistry = [];
        List<OfflineRegistryDelta> registryDeltas = [];
        foreach ((string hive, string hiveRelativePath, string root) in PublicationAnalysisScope.RegistryRoots)
        {
            OfflineRegistrySnapshot before = registryEngine.Capture(hive, Path.Combine(baselineRoot, hiveRelativePath), root);
            OfflineRegistrySnapshot after = registryEngine.Capture(hive, Path.Combine(servicedRoot, hiveRelativePath), root);
            baselineRegistry.Add(before);
            servicedRegistry.Add(after);
            registryDeltas.AddRange(registryEngine.Compare(before, after));
        }

        DriverStagingPlan stagingPlan = new DriverStagingPlanner().Create(package);
        string infPath = stagingPlan.StoreFiles.Single(file => file.FileName.Equals(stagingPlan.Package.Inf, StringComparison.OrdinalIgnoreCase)).SourcePath;
        InfInspection inspection = new InfInspector().Inspect(infPath);
        string? catalogPath = stagingPlan.Package.Catalog is null ? null : stagingPlan.StoreFiles.Single(file => file.FileName.Equals(stagingPlan.Package.Catalog, StringComparison.OrdinalIgnoreCase)).SourcePath;
        PublicationSourcePackage sourcePackage = new(
            package,
            Path.GetFileName(Path.TrimEndingDirectorySeparator(package)),
            infPath,
            OfflineFileSnapshotEngine.Hash(infPath),
            catalogPath,
            catalogPath is null ? null : OfflineFileSnapshotEngine.Hash(catalogPath),
            stagingPlan.Package.Class,
            stagingPlan.Package.ClassGuid,
            stagingPlan.Package.Provider,
            stagingPlan.Package.DriverVersion,
            stagingPlan.Package.InstallSections,
            stagingPlan.DeviceIds,
            inspection.CopyOperations.Any(copy => copy.DestinationDirectoryId == 13),
            inspection.ServiceOperations.Length > 0,
            inspection.ExtensionId,
            inspection.HasAddSoftware,
            inspection.SoftwareComponentIds,
            stagingPlan.Reflection.Copies.Select(copy => copy.DestinationPath).ToArray(),
            stagingPlan.Reflection.Services.Select(service => service.Name).ToArray());

        DriverPublicationObservation observation = BuildObservation(sourcePackage, stagingPlan, baselineFiles, servicedFiles, fileDeltas, [.. registryDeltas]);
        ServiceFieldComparison[] serviceComparisons = CompareServices(stagingPlan, servicedRegistry);
        string[] contradictions = serviceComparisons.Where(comparison => comparison.Status == ServiceComparisonStatus.Contradiction).Select(comparison => $"{comparison.ServiceName}/{comparison.ValueName}: {comparison.Detail}").ToArray();
        PublicationAnalysisReport report = new(
            baseline,
            serviced,
            imageIndex,
            workspace,
            sourcePackage,
            fileDeltas,
            [.. registryDeltas],
            observation,
            serviceComparisons,
            [.. Unresolved],
            contradictions);

        string reportPath = Path.Combine(workspace, "publication-analysis.json");
        using FileStream reportStream = File.Create(reportPath);
        JsonSerializer.Serialize(reportStream, report, PublicationAnalysisJsonContext.Default.PublicationAnalysisReport);
        return report;
    }

    private static DriverPublicationObservation BuildObservation(
        PublicationSourcePackage source,
        DriverStagingPlan plan,
        OfflineFileSnapshot beforeFiles,
        OfflineFileSnapshot afterFiles,
        OfflineFileDelta[] fileDeltas,
        OfflineRegistryDelta[] registryDeltas)
    {
        const string repositoryPrefix = @"Windows\System32\DriverStore\FileRepository\";
        string[] repositories = fileDeltas.Where(delta => delta.Change == OfflineFileChange.Added && delta.Path.StartsWith(repositoryPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(delta => delta.Path[repositoryPrefix.Length..].Split(Path.DirectorySeparatorChar)[0])
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] baselineInfs = PublishedInfs(beforeFiles);
        string[] servicedInfs = PublishedInfs(afterFiles);
        string[] newInfs = servicedInfs.Except(baselineInfs, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        PublishedFileObservation[] infPublications = newInfs.Select(path => BuildPublishedObservation(path, source.Inf, repositories, afterFiles)).ToArray();

        string[] publishedCatalogPaths = fileDeltas.Where(delta => delta.Change == OfflineFileChange.Added && IsCatRootPath(delta.Path) && IsOemFile(delta.Path, ".cat"))
            .Select(delta => delta.Path).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        PublishedFileObservation[] catalogPublications = source.Catalog is null
            ? []
            : publishedCatalogPaths.Select(path => BuildPublishedObservation(path, source.Catalog, repositories, afterFiles)).ToArray();
        string[] modifiedCatalogFiles = fileDeltas.Where(delta => delta.Change == OfflineFileChange.Modified && IsCatRootPath(delta.Path)).Select(delta => delta.Path).ToArray();

        OfflineRegistryDelta[] deviceIds = RegistryWhere(registryDeltas, @"DriverDatabase\DeviceIds");
        OfflineRegistryDelta[] driverInfFiles = RegistryWhere(registryDeltas, @"DriverDatabase\DriverInfFiles");
        OfflineRegistryDelta[] driverPackages = RegistryWhere(registryDeltas, @"DriverDatabase\DriverPackages");
        OfflineRegistryDelta[] ownership = registryDeltas.Where(delta => delta.Hive.Equals("SOFTWARE", StringComparison.OrdinalIgnoreCase)).ToArray();
        OfflineRegistryDelta[] services = plan.Reflection.Services.SelectMany(service => registryDeltas.Where(delta => delta.Hive.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) && delta.KeyPath.StartsWith(@"ControlSet001\Services\" + service.Name, StringComparison.OrdinalIgnoreCase))).Distinct().ToArray();
        HashSet<OfflineRegistryDelta> categorized = [.. deviceIds, .. driverInfFiles, .. driverPackages, .. ownership, .. services];
        OfflineRegistryDelta[] otherRegistry = registryDeltas.Where(delta => !categorized.Contains(delta)).ToArray();
        string[] databaseHives = driverPackages.Select(delta => delta.Hive).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

        HashSet<string> recognizedFiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in newInfs.Concat(publishedCatalogPaths)) recognizedFiles.Add(path);
        foreach (OfflineFileDelta delta in fileDeltas.Where(delta => delta.Path.StartsWith(repositoryPrefix, StringComparison.OrdinalIgnoreCase))) recognizedFiles.Add(delta.Path);
        foreach (string reflected in plan.Reflection.Copies.Select(copy => copy.DestinationPath)) recognizedFiles.Add(reflected);
        OfflineFileDelta[] otherFiles = fileDeltas.Where(delta => delta.Change != OfflineFileChange.Unchanged && !recognizedFiles.Contains(delta.Path)).ToArray();

        return new DriverPublicationObservation(
            source.RepositoryIdentity,
            repositories,
            repositories.Contains(source.RepositoryIdentity, StringComparer.OrdinalIgnoreCase),
            "unsupported",
            baselineInfs,
            servicedInfs,
            newInfs,
            infPublications,
            catalogPublications,
            modifiedCatalogFiles,
            databaseHives,
            deviceIds,
            driverInfFiles,
            driverPackages,
            ownership,
            services,
            otherRegistry,
            otherFiles);
    }

    private static PublishedFileObservation BuildPublishedObservation(string publishedPath, string sourcePath, string[] repositories, OfflineFileSnapshot afterFiles)
    {
        OfflineFileState published = afterFiles.Files.Single(file => file.Path.Equals(publishedPath, StringComparison.OrdinalIgnoreCase));
        string sourceHash = OfflineFileSnapshotEngine.Hash(sourcePath);
        string fileName = Path.GetFileName(sourcePath);
        OfflineFileState? repository = afterFiles.Files.FirstOrDefault(file => repositories.Any(repo => file.Path.Equals($@"Windows\System32\DriverStore\FileRepository\{repo}\{fileName}", StringComparison.OrdinalIgnoreCase)));
        return new PublishedFileObservation(published.Path, published.Sha256, sourcePath, sourceHash, published.Sha256.Equals(sourceHash, StringComparison.Ordinal), repository?.Path, repository?.Sha256, repository is null ? null : published.Sha256.Equals(repository.Sha256, StringComparison.Ordinal));
    }

    private static ServiceFieldComparison[] CompareServices(DriverStagingPlan plan, IReadOnlyList<OfflineRegistrySnapshot> servicedSnapshots)
    {
        OfflineRegistrySnapshot servicesSnapshot = servicedSnapshots.Single(snapshot => snapshot.Hive == "SYSTEM" && snapshot.RootPath.Equals(@"ControlSet001\Services", StringComparison.OrdinalIgnoreCase));
        List<ServiceFieldComparison> comparisons = [];
        foreach (ReflectedService service in plan.Reflection.Services)
        {
            string keyPath = @"ControlSet001\Services\" + service.Name;
            OfflineRegistryKeyState? key = servicesSnapshot.Keys.FirstOrDefault(candidate => candidate.Path.Equals(keyPath, StringComparison.OrdinalIgnoreCase));
            Dictionary<string, string> expected = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Type"] = service.Type.ToString(),
                ["Start"] = service.Start.ToString(),
                ["ErrorControl"] = service.ErrorControl.ToString(),
                ["ImagePath"] = service.ImagePath
            };
            foreach ((string name, string plannedValue) in expected)
            {
                OfflineRegistryValueState? observed = key?.Values.FirstOrDefault(value => value.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                bool matches = observed?.Decoded?.Equals(plannedValue, StringComparison.OrdinalIgnoreCase) == true;
                comparisons.Add(new ServiceFieldComparison(service.Name, name, matches ? ServiceComparisonStatus.DerivedCorrectly : ServiceComparisonStatus.Contradiction, plannedValue, observed, matches ? "Observed value matches DriverStagingPlan." : "Observed value is missing or differs from DriverStagingPlan."));
            }
            foreach (OfflineRegistryValueState extra in key?.Values.Where(value => !expected.ContainsKey(value.Name)) ?? [])
                comparisons.Add(new ServiceFieldComparison(service.Name, extra.Name, ServiceComparisonStatus.ObservedExtraServicingMetadata, null, extra, "Observed servicing metadata is not part of DriverStagingPlan."));
        }
        return [.. comparisons];
    }

    private static OfflineRegistryDelta[] RegistryWhere(OfflineRegistryDelta[] deltas, string prefix) => deltas.Where(delta => delta.KeyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
    private static string[] PublishedInfs(OfflineFileSnapshot snapshot) => snapshot.Files.Where(file => file.Path.StartsWith(@"Windows\INF\", StringComparison.OrdinalIgnoreCase) && IsOemFile(file.Path, ".inf")).Select(file => file.Path).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    private static bool IsCatRootPath(string path) => path.StartsWith(@"Windows\System32\CatRoot\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(@"Windows\System32\CatRoot2\", StringComparison.OrdinalIgnoreCase);
    private static bool IsOemFile(string path, string extension)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase) && name.Length > 3 && name.StartsWith("oem", StringComparison.OrdinalIgnoreCase) && name.AsSpan(3).IndexOfAnyExceptInRange('0', '9') < 0;
    }
}
