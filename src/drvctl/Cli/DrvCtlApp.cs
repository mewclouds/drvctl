using DrvCtl.Benchmarking;
using DrvCtl.Analysis;
using DrvCtl.Copy;
using DrvCtl.Core;
using DrvCtl.Dism;
using DrvCtl.Drivers;
using DrvCtl.Export;
using DrvCtl.Images;
using DrvCtl.Offline;
using DrvCtl.Platform;
using DrvCtl.Publication;
using DrvCtl.Utilities;
using DrvCtl.Validation;
using DrvCtl.Verification;

namespace DrvCtl.Cli;

/*
 * The command dispatch table and top-level error handling for every drvctl
 * command, public and hidden research alike. RunExportAsync and RunList
 * back the public CLI. The RunInspectInf/RunInspectWim/RunPlanDriver/
 * RunValidatePlan/RunSimulateApply/RunAnalyzePublication/
 * RunPrototypePublication/RunPrototypeInjectWim methods below them each back
 * one hidden research command and print their own raw, technical output
 * since they have no friendly/verbose split to maintain.
 */

internal static class DrvCtlApp
{
    /// Parses argv, dispatches to the matching command, and converts thrown
    /// exceptions into the appropriate exit code: UsageException becomes a
    /// usage error plus general help, CommandHelpException prints
    /// command-specific help, anything else is reported as a runtime failure.
    internal static async Task<int> RunAsync(
        string[] args
    )
    {
        try
        {
            CommandOptions options =
                CommandLine.Parse(args);

            if (options is HelpCommandOptions)
            {
                HelpText.PrintGeneral();
                return ExitCodes.Success;
            }

            return options switch
            {
                ExportCommandOptions exportOptions =>
                    await RunExportAsync(
                        CreateExporter(),
                        exportOptions
                    ),

                ListCommandOptions listOptions =>
                    RunList(
                        new DriverStoreResolver(),
                        listOptions
                    ),

                InspectInfCommandOptions inspectInfOptions => RunInspectInf(inspectInfOptions),
                InspectWimCommandOptions inspectWimOptions => RunInspectWim(inspectWimOptions),
                PlanDriverCommandOptions planDriverOptions => RunPlanDriver(planDriverOptions),
                ValidatePlanCommandOptions validatePlanOptions => RunValidatePlan(validatePlanOptions),
                SimulateApplyCommandOptions simulateApplyOptions => RunSimulateApply(simulateApplyOptions),
                AnalyzePublicationCommandOptions analyzePublicationOptions => RunAnalyzePublication(analyzePublicationOptions),
                PrototypePublicationCommandOptions prototypePublicationOptions => RunPrototypePublication(prototypePublicationOptions),
                PrototypeInjectWimCommandOptions prototypeInjectWimOptions => RunPrototypeInjectWim(prototypeInjectWimOptions),

                _ =>
                    throw new InvalidOperationException(
                        "Unsupported command."
                    )
            };
        }
        catch (CommandHelpException help)
        {
            if (help.Command.Equals("export", StringComparison.OrdinalIgnoreCase))
            {
                HelpText.PrintExport();
            }
            else
            {
                HelpText.PrintList();
            }

            return ExitCodes.Success;
        }
        catch (UsageException error)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(error.Message);
            HelpText.PrintGeneral();
            return ExitCodes.UsageFailure;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("drvctl failed:");
            Console.Error.WriteLine($"  {error.Message}");
            return ExitCodes.RuntimeFailure;
        }
    }

    // The Run* methods below back hidden research commands (see file
    // header). None of them are reachable from HelpText or the public
    // export/list surface.

    private static int RunPrototypePublication(PrototypePublicationCommandOptions options)
    {
        PublicationPrototypeResult result = new PublicationPrototypeRunner().Run(options.BaselineWim, options.ReferenceWim, options.PackageDirectory, options.ImageIndex, options.Workspace);
        Console.WriteLine("Disposable publication prototype");
        Console.WriteLine($"  Status: {(result.Complete ? "Complete" : "Prototype incomplete")}");
        Console.WriteLine($"  Workspace: {result.Workspace}");
        Console.WriteLine($"  Result: {Path.Combine(result.Workspace, "publication-prototype-result.json")}");
        Console.WriteLine($"  Repository: {result.Plan.RepositoryIdentity}");
        Console.WriteLine($"  Repository identity source: {result.Plan.RepositoryIdentitySource}");
        Console.WriteLine($"  Computed repository identity: {result.Plan.ComputedRepositoryIdentity}");
        Console.WriteLine($"  Published INF: {result.Plan.PublishedInf}");
        Console.WriteLine($"  Published catalog: {result.Plan.PublishedCatalog}");
        Console.WriteLine($"  DriverDatabase hive: {result.Plan.DriverDatabaseHive}");
        Console.WriteLine($"  Allocation: {result.Plan.AllocationRule}");
        Console.WriteLine("OemInfMap validation");
        foreach (OemInfMapValidation validation in result.Plan.OemInfMapValidation)
            Console.WriteLine($"  [{string.Join(',', validation.OccupiedIndexes)}] expected={validation.ExpectedHex} actual={validation.ActualHex} match={validation.Matches}");
        Console.WriteLine("Applied files");
        foreach (PublicationAppliedFile file in result.AppliedFiles)
            Console.WriteLine($"  {Path.GetRelativePath(result.TreeRoot, file.DestinationPath)} size={file.Size} sha256={file.DestinationSha256} match={file.Matches}");
        Console.WriteLine("Applied registry");
        foreach (PublicationAppliedRegistryValue value in result.AppliedRegistryValues)
            Console.WriteLine($"  {value.Hive}\\{value.KeyPath}\\{value.Name} {value.TypeName} raw={value.RawHex} status={value.EvidenceStatus} derivation={value.Derivation}");
        Console.WriteLine("Omitted operations");
        foreach (PublicationOmittedOperation operation in result.Plan.OmittedOperations)
            Console.WriteLine($"  {operation.Target} [{operation.EvidenceStatus}]: {operation.Reason}");
        Console.WriteLine("Semantic comparison");
        Console.WriteLine($"  Exact matches: {result.Comparison.ExactMatches}");
        Console.WriteLine($"  Semantically equivalent: {result.Comparison.SemanticallyEquivalent}");
        Console.WriteLine($"  Expected differences: {result.Comparison.ExpectedDifferences}");
        Console.WriteLine($"  Unsupported: {result.Comparison.Unsupported}");
        Console.WriteLine($"  Contradictions: {result.Comparison.Contradictions}");
        foreach (PublicationComparisonItem item in result.Comparison.Items.Where(item => item.Status != PublicationComparisonStatus.SemanticallyEquivalent))
            Console.WriteLine($"  {item.Status}: {item.Category} {item.Identity} - {item.Detail}");
        Console.WriteLine("Assessments");
        Console.WriteLine($"  Offline servicing: {result.OfflineServicingAssessment}");
        Console.WriteLine($"  Full publication:  {result.FullPublicationAssessment}");
        Console.WriteLine($"Baseline SHA256 before: {result.BaselineSha256Before}");
        Console.WriteLine($"Baseline SHA256 after:  {result.BaselineSha256After}");
        foreach (PublicationSourceHash source in result.SourcePackageHashes)
            Console.WriteLine($"Package source unchanged: {Path.GetFileName(source.Path)} {source.Unchanged} {source.BeforeSha256}");
        return result.Complete ? ExitCodes.Success : ExitCodes.VerificationMismatch;
    }

    private static int RunPrototypeInjectWim(PrototypeInjectWimCommandOptions options)
    {
        WimPublicationResult result = new WimDriverInjector().Inject(options.BaselineWim, options.OutputWim, options.PackageDirectory, options.ImageIndex, options.Workspace, options.SkipSelfVerification);
        Console.WriteLine("Direct copied-WIM publication prototype");
        Console.WriteLine($"  Status: {(result.WimMutationSuccess ? "Success" : "Failed")}");
        Console.WriteLine($"  Baseline: {result.BaselineWim}");
        Console.WriteLine($"  Output WIM: {result.OutputWim}");
        Console.WriteLine($"  Output size: {result.OutputSizeBytes:N0} bytes");
        Console.WriteLine($"  Output SHA256: {result.OutputSha256}");
        Console.WriteLine($"  Workspace: {result.Workspace}");
        Console.WriteLine($"  Result JSON: {Path.Combine(result.Workspace, "wim-publication-result.json")}");
        Console.WriteLine("Timings");
        Console.WriteLine($"  Baseline copy:             {result.Timings.BaselineCopyMs} ms");
        Console.WriteLine($"  Plan generation:           {result.Timings.PlanMs} ms");
        Console.WriteLine($"  Hive extraction:           {result.Timings.HiveExtractMs} ms");
        Console.WriteLine($"  Hive mutation:             {result.Timings.HiveMutationMs} ms");
        Console.WriteLine($"  WIM update prep:           {result.Timings.WimUpdatePreparationMs} ms");
        Console.WriteLine($"  WIM write (overwrite):     {result.Timings.WimWriteMs} ms");
        Console.WriteLine($"  Self-verification:         {result.Timings.SelfVerificationMs} ms");
        Console.WriteLine($"  Total command duration:    {result.Timings.TotalCommandMs} ms");
        Console.WriteLine("Self-verification");
        Console.WriteLine($"  WIM opens:                 {result.SelfVerification.WimOpens}");
        Console.WriteLine($"  Image count:               {result.SelfVerification.ImageCount}");
        Console.WriteLine($"  Image name:                {result.SelfVerification.ImageName}");
        Console.WriteLine($"  Verified files:            {result.SelfVerification.VerifiedFileCount}/{result.GeneratedFiles.Length} (iterated={result.SelfVerification.FileOperationsIteratedCount}, distinct={result.SelfVerification.FileOperationsDistinctCount}) match={result.SelfVerification.AllFilesMatch}");
        Console.WriteLine($"  Verified registry values:  {result.SelfVerification.VerifiedRegistryCount}/{result.GeneratedRegistryValues.Length} (iterated={result.SelfVerification.RegistryValuesIteratedCount}, distinct={result.SelfVerification.RegistryValuesDistinctCount}) match={result.SelfVerification.AllRegistryMatches}");
        Console.WriteLine($"  Self-verification valid:   {result.SelfVerification.Valid}");
        if (result.SelfVerification.Diagnostics.Length > 0)
        {
            Console.WriteLine("Self-verification diagnostics");
            int diagLimit = Math.Min(20, result.SelfVerification.Diagnostics.Length);
            for (int i = 0; i < diagLimit; i++)
            {
                var diag = result.SelfVerification.Diagnostics[i];
                Console.WriteLine($"  [{i + 1}] {diag.Phase}/{diag.FailureKind}: {diag.ExpectedPath}");
                if (diag.ProbeLocation != null) Console.WriteLine($"        Probe: {diag.ProbeLocation}");
                if (diag.ExceptionType != null) Console.WriteLine($"        Exception: {diag.ExceptionType}: {diag.ExceptionMessage}");
                if (diag.ExpectedSha256 != null && diag.ActualSha256 != null)
                    Console.WriteLine($"        Expected SHA256: {diag.ExpectedSha256}, Actual: {diag.ActualSha256}");
            }
            if (result.SelfVerification.Diagnostics.Length > 20)
                Console.WriteLine($"  ... and {result.SelfVerification.Diagnostics.Length - 20} more diagnostic entries (see wim-publication-result.json)");
        }
        Console.WriteLine("Generated files in WIM");
        foreach (string file in result.GeneratedFiles) Console.WriteLine($"  + {file}");
        Console.WriteLine("Omitted unsupported operations");
        foreach (string op in result.OmittedOperations) Console.WriteLine($"  - {op}");
        Console.WriteLine($"Baseline SHA256 before: {result.BaselineSha256Before}");
        Console.WriteLine($"Baseline SHA256 after:  {result.BaselineSha256After}");
        return result.WimMutationSuccess ? ExitCodes.Success : ExitCodes.VerificationMismatch;
    }

    private static int RunAnalyzePublication(AnalyzePublicationCommandOptions options)
    {
        PublicationAnalysisReport report = new DriverPublicationAnalyzer().Analyze(options.BaselineWim, options.ServicedWim, options.PackageDirectory, options.ImageIndex, options.Workspace);
        Console.WriteLine("Publication analysis");
        Console.WriteLine($"  Baseline: {report.BaselineWim}");
        Console.WriteLine($"  Serviced: {report.ServicedWim}");
        Console.WriteLine($"  Image index: {report.ImageIndex}");
        Console.WriteLine($"  Package: {report.SourcePackage.Directory}");
        Console.WriteLine($"  Report: {Path.Combine(report.Workspace, "publication-analysis.json")}");
        Console.WriteLine("FileRepository identities");
        Console.WriteLine($"  Known source: {report.Observation.KnownSourceRepositoryIdentity}");
        foreach (string identity in report.Observation.ObservedServicedRepositoryIdentities) Console.WriteLine($"  Observed serviced: {identity}");
        Console.WriteLine($"  Source identity observed: {report.Observation.SourceIdentityMatchesObserved}");
        Console.WriteLine("  Computed identity: unsupported");
        Console.WriteLine("OEM INF publication");
        Console.WriteLine($"  Baseline: {FormatList(report.Observation.BaselinePublishedInfs)}");
        Console.WriteLine($"  Serviced: {FormatList(report.Observation.ServicedPublishedInfs)}");
        Console.WriteLine($"  Newly introduced: {FormatList(report.Observation.NewPublishedInfs)}");
        foreach (PublishedFileObservation publication in report.Observation.InfPublications) PrintPublication(publication);
        Console.WriteLine("Catalog publication");
        foreach (PublishedFileObservation publication in report.Observation.CatalogPublications) PrintPublication(publication);
        Console.WriteLine($"  Modified pre-existing CatRoot/CatRoot2 files: {FormatList(report.Observation.ModifiedExistingCatRootFiles)}");
        Console.WriteLine($"DriverDatabase hives with logical deltas: {FormatList(report.Observation.SelectedDriverDatabaseHives)}");
        Console.WriteLine("Filesystem delta");
        foreach (OfflineFileDelta delta in report.FileDeltas.Where(delta => delta.Change != OfflineFileChange.Unchanged))
            Console.WriteLine($"  {delta.Change}: {delta.Path} before={delta.Before?.Sha256 ?? "-"} after={delta.After?.Sha256 ?? "-"}");
        Console.WriteLine("Registry delta");
        foreach (OfflineRegistryDelta delta in report.RegistryDeltas)
        {
            Console.WriteLine($"  {delta.Change}: {delta.Hive}\\{delta.KeyPath}{(delta.ValueName is null ? string.Empty : "\\" + delta.ValueName)}");
            if (delta.BeforeValue is not null) Console.WriteLine($"    Before: {delta.BeforeValue.TypeName} raw={delta.BeforeValue.RawHex} decoded={Decoded(delta.BeforeValue)}");
            if (delta.AfterValue is not null) Console.WriteLine($"    After: {delta.AfterValue.TypeName} raw={delta.AfterValue.RawHex} decoded={Decoded(delta.AfterValue)}");
        }
        Console.WriteLine("Service comparison");
        foreach (ServiceFieldComparison comparison in report.ServiceComparisons)
            Console.WriteLine($"  {comparison.Status}: {comparison.ServiceName}\\{comparison.ValueName} planned={comparison.PlannedValue ?? "-"} observed={(comparison.ObservedValue is null ? "-" : Decoded(comparison.ObservedValue))}");
        Console.WriteLine($"Contradictions: {report.Contradictions.Length}");
        Console.WriteLine("Unresolved semantics");
        foreach (string unresolved in report.UnresolvedObservations) Console.WriteLine($"  {unresolved}");
        return report.Contradictions.Length == 0 ? ExitCodes.Success : ExitCodes.VerificationMismatch;
    }

    private static void PrintPublication(PublishedFileObservation publication)
    {
        Console.WriteLine($"  {publication.PublishedPath}");
        Console.WriteLine($"    Published SHA256: {publication.PublishedSha256}");
        Console.WriteLine($"    Source: {publication.SourcePath}");
        Console.WriteLine($"    Source SHA256: {publication.SourceSha256}");
        Console.WriteLine($"    Source match: {publication.SourceHashMatches}");
        Console.WriteLine($"    Repository: {publication.RepositoryPath ?? "not observed"}");
        Console.WriteLine($"    Repository SHA256: {publication.RepositorySha256 ?? "-"}");
        Console.WriteLine($"    Repository match: {publication.RepositoryHashMatches?.ToString() ?? "unsupported"}");
    }

    private static string Decoded(OfflineRegistryValueState value) => value.Decoded ?? (value.DecodedStrings is null ? "(raw only)" : "[" + string.Join(" | ", value.DecodedStrings) + "]");
    private static string FormatList(IReadOnlyList<string> values) => values.Count == 0 ? "none" : string.Join(", ", values);

    private static int RunSimulateApply(SimulateApplyCommandOptions options)
    {
        OfflineApplyPlan applyPlan = new OfflineApplyPlanner().Create(options.PackageDirectory, options.Workspace, options.SystemHive, options.SoftwareHive, options.DriversHive);
        OfflineApplyResult result = new OfflineApplyExecutor(new CopyFile2Engine()).Execute(applyPlan);
        OfflineApplyFixtureComparison fixture = new OfflineApplyFixtureValidator().Validate(result);

        Console.WriteLine("Simulation");
        Console.WriteLine($"  Package: {Path.GetFileName(Path.TrimEndingDirectorySeparator(result.Plan.SourcePlan.Package.Directory))}");
        Console.WriteLine($"  Workspace: {result.Plan.Workspace}");
        Console.WriteLine($"  Control set: {result.Plan.ControlSet}");
        Console.WriteLine($"  {result.Plan.ControlSetLimitation}");
        Console.WriteLine("Applied files");
        if (result.Files.Length == 0) Console.WriteLine("  None");
        foreach (OfflineFileCopyResult file in result.Files)
        {
            Console.WriteLine($"  {Path.GetFileName(file.SourcePath)}");
            Console.WriteLine($"    -> {Path.GetRelativePath(result.Plan.Workspace, file.OutputPath)}");
            Console.WriteLine($"    Source size: {file.SourceSize}");
            Console.WriteLine($"    Source SHA256: {file.SourceSha256}");
            Console.WriteLine($"    Output size: {file.OutputSize}");
            Console.WriteLine($"    Output SHA256: {file.OutputSha256}");
            Console.WriteLine($"    Match: {file.Matches}");
        }
        Console.WriteLine("Applied registry");
        if (result.RegistryWrites.Length == 0) Console.WriteLine("  None");
        foreach (IGrouping<string, OfflineRegistryWriteResult> key in result.RegistryWrites.GroupBy(write => $"{write.Hive}\\{write.KeyPath}"))
        {
            Console.WriteLine($"  {key.Key}");
            foreach (OfflineRegistryWriteResult value in key) Console.WriteLine($"    {value.Name} ({value.Type}) = {value.Value}");
        }
        Console.WriteLine("Input hive protection");
        foreach (OfflineHiveResult hive in result.Hives)
        {
            Console.WriteLine($"  {hive.Name}: unchanged={hive.SourceUnchanged}");
            Console.WriteLine($"    Source SHA256 before: {hive.SourceSha256Before}");
            Console.WriteLine($"    Source SHA256 after:  {hive.SourceSha256After}");
            Console.WriteLine($"    Output SHA256:        {hive.OutputSha256}");
            Console.WriteLine($"    Output: {hive.OutputPath}");
        }
        Console.WriteLine("Skipped as unresolved");
        foreach (OfflineSkippedOperation skipped in result.Plan.SkippedOperations) Console.WriteLine($"  {skipped.Name}: {skipped.Reason}");
        Console.WriteLine("Post-apply verification");
        foreach (OfflineVerificationResult verification in result.Verification) Console.WriteLine($"  {(verification.Succeeded ? "Passed" : "FAILED")}: {verification.Name} - {verification.Detail}");
        Console.WriteLine($"Fixture comparison: {fixture.FixtureName}");
        foreach (OfflineApplyFixtureResult comparison in fixture.Results)
        {
            string status = comparison.Status switch
            {
                OfflineApplyFixtureStatus.MatchedAppliedSubset => "Matched applied subset",
                OfflineApplyFixtureStatus.ExpectedUnresolvedDifference => "Expected unresolved difference",
                OfflineApplyFixtureStatus.Contradiction => "Contradiction",
                _ => throw new InvalidOperationException("Unknown offline fixture status.")
            };
            Console.WriteLine($"  {status}: {comparison.Name} - {comparison.Detail}");
        }
        Console.WriteLine("Output files");
        foreach (string output in result.OutputFiles) Console.WriteLine($"  {Path.GetRelativePath(result.Plan.Workspace, output)}");
        return result.VerificationSucceeded && !fixture.HasContradictions ? ExitCodes.Success : ExitCodes.VerificationMismatch;
    }

    private static int RunValidatePlan(ValidatePlanCommandOptions options)
    {
        DriverPlanValidation validation = new DriverPlanValidator().Validate(options.PackageDirectory);
        Console.WriteLine($"Fixture: {validation.Fixture.Name}");
        Console.WriteLine($"Package: {validation.Plan.Package.Directory}");
        foreach (SemanticValidationResult result in validation.Results)
        {
            string status = result.Status switch
            {
                SemanticValidationStatus.DerivedCorrectly => "Derived correctly",
                SemanticValidationStatus.ObservedButUnresolved => "Observed but unresolved",
                SemanticValidationStatus.Contradiction => "Contradiction",
                _ => throw new InvalidOperationException("Unknown semantic validation status.")
            };
            Console.WriteLine($"{status}: {result.Name}");
            Console.WriteLine($"  {result.Detail}");
        }
        int contradictions = validation.Results.Count(result => result.Status == SemanticValidationStatus.Contradiction);
        Console.WriteLine($"Summary: {validation.Results.Length - contradictions} non-contradictions, {contradictions} contradictions");
        return validation.HasContradictions ? ExitCodes.VerificationMismatch : ExitCodes.Success;
    }

    private static int RunPlanDriver(PlanDriverCommandOptions options)
    {
        DriverStagingPlan plan = new DriverStagingPlanner().Create(options.PackageDirectory);
        Console.WriteLine("Package");
        Console.WriteLine($"  Directory: {plan.Package.Directory}");
        Console.WriteLine($"  INF: {plan.Package.Inf}");
        Console.WriteLine($"  Class: {plan.Package.Class ?? "unsupported"}");
        Console.WriteLine($"  ClassGuid: {plan.Package.ClassGuid ?? "unsupported"}");
        Console.WriteLine($"  Provider: {plan.Package.Provider ?? "unsupported"}");
        Console.WriteLine($"  DriverVer: {plan.Package.DriverVersion ?? "unsupported"}");
        Console.WriteLine($"  Catalog: {plan.Package.Catalog ?? "not declared"}");
        Console.WriteLine($"  Architecture: {plan.Package.Architecture}");
        Console.WriteLine("  Install sections:");
        foreach (string section in plan.Package.InstallSections) Console.WriteLine($"    {section}");

        Console.WriteLine("Store files");
        foreach (StoreFilePlan file in plan.StoreFiles) Console.WriteLine($"  {file.FileName}");

        Console.WriteLine("Published INF");
        Console.WriteLine($"  Source: {plan.PublishedInf.SourceFile}");
        Console.WriteLine($"  Published identity: {plan.PublishedInf.PublishedIdentity ?? "unresolved"}");
        Console.WriteLine("Published catalog");
        Console.WriteLine($"  Source: {plan.PublishedCatalog.SourceFile}");
        Console.WriteLine($"  Published identity: {plan.PublishedCatalog.PublishedIdentity ?? "unresolved"}");

        Console.WriteLine("Device IDs");
        foreach (string deviceId in plan.DeviceIds) Console.WriteLine($"  {deviceId}");

        Console.WriteLine("Reflection");
        Console.WriteLine("  Copy");
        foreach (ReflectedFileCopy copy in plan.Reflection.Copies)
        {
            Console.WriteLine($"    Section: {copy.InstallSection}");
            Console.WriteLine($"    {copy.SourceFile}");
            Console.WriteLine($"      -> {copy.DestinationPath}");
        }
        Console.WriteLine("  Service");
        foreach (ReflectedService service in plan.Reflection.Services)
        {
            Console.WriteLine($"    {service.Name}");
            Console.WriteLine($"      Install section: {service.InstallSection}");
            Console.WriteLine($"      Services section: {service.ServicesSection}");
            Console.WriteLine($"      Configuration section: {service.ConfigurationSection}");
            Console.WriteLine($"      Type: {service.Type}");
            Console.WriteLine($"      Start: {service.Start}");
            Console.WriteLine($"      ErrorControl: {service.ErrorControl}");
            Console.WriteLine($"      ImagePath: {service.ImagePath}");
        }

        Console.WriteLine("DriverDatabase");
        Console.WriteLine($"  Target hive: {plan.DriverDatabase.TargetHive ?? "unresolved"}");
        Console.WriteLine($"  Representation: {plan.DriverDatabase.Representation ?? "unresolved"}");
        Console.WriteLine("Unresolved");
        foreach (string operation in plan.UnresolvedOperations) Console.WriteLine($"  {operation}");
        return ExitCodes.Success;
    }

    private static int RunInspectInf(InspectInfCommandOptions options)
    {
        InfInspection inspection = new InfInspector().Inspect(options.Path);
        Console.WriteLine($"INF path: {inspection.Path}");
        Console.WriteLine($"Class: {inspection.Class ?? "unsupported"}");
        Console.WriteLine($"ClassGuid: {inspection.ClassGuid ?? "unsupported"}");
        Console.WriteLine($"Provider: {inspection.Provider ?? "unsupported"}");
        Console.WriteLine($"CatalogFile: {inspection.CatalogFile ?? "unsupported"}");
        Console.WriteLine($"DriverVer: {inspection.DriverVersion ?? "unsupported"}");
        Console.WriteLine($"ExtensionId: {inspection.ExtensionId ?? "not declared"}");
        Console.WriteLine($"AddSoftware: {inspection.HasAddSoftware}");
        Console.WriteLine($"PnpLockdown: {inspection.PnpLockdown?.ToString() ?? "not declared"}");
        Console.WriteLine($"AMD64 models section: {inspection.ModelsSection ?? "unsupported"}");
        PrintValues("Selected AMD64 install sections", inspection.InstallSections);
        PrintValues("CopyFiles directives", inspection.CopyFilesDirectives);
        PrintValues("AddService directives", inspection.AddServiceDirectives);
        PrintValues("Hardware/model IDs", inspection.HardwareIds);
        PrintValues("Software component IDs", inspection.SoftwareComponentIds);
        Console.WriteLine("Model entries:");
        foreach (InfModelEntry model in inspection.Models) Console.WriteLine($"  {model.Description} -> {model.InstallSection}: {string.Join(", ", model.Ids)}; manufacturer={model.Manufacturer}");
        Console.WriteLine("Service metadata:");
        foreach (InfServiceOperation service in inspection.ServiceOperations) Console.WriteLine($"  {service.Name}|{service.InstallSection}|{service.ConfigurationSection}|{service.DisplayName ?? string.Empty}|{service.ServiceType}|{service.StartType}|{service.ErrorControl}|{service.ServiceBinary}");
        Console.WriteLine("Copy operations:");
        foreach (InfCopyOperation copy in inspection.CopyOperations) Console.WriteLine($"  {copy.InstallSection}|{copy.SourceFile}|{copy.DestinationFile}|{copy.DestinationDirectoryId}|{copy.DestinationSubdirectory ?? string.Empty}");
        Console.WriteLine("INF strings:");
        foreach (InfStringValue value in inspection.Strings) Console.WriteLine($"  {value.Name}={value.Value}");
        return ExitCodes.Success;
    }

    private static int RunInspectWim(InspectWimCommandOptions options)
    {
        using WimImage wim = WimImage.Open(options.Path);
        Console.WriteLine($"WIM path: {wim.Path}");
        Console.WriteLine($"Image count: {wim.ImageCount}");
        Console.WriteLine($"Boot index: {wim.BootIndex}");
        Console.WriteLine($"WIM version: 0x{wim.WimVersion:X8}");
        Console.WriteLine($"Compression type: {wim.CompressionType}");
        Console.WriteLine($"Chunk size: {wim.ChunkSize}");
        Console.WriteLine($"Part: {wim.PartNumber} of {wim.TotalParts}");
        for (int index = 1; index <= wim.ImageCount; index++)
        {
            WimImageMetadata image = wim.InspectImage(index);
            Console.WriteLine($"Image {image.Index}: {image.Name ?? "(unnamed)"}");
            if (!string.IsNullOrWhiteSpace(image.Description)) Console.WriteLine($"  Description: {image.Description}");
        }
        return ExitCodes.Success;
    }

    private static void PrintValues(string label, IReadOnlyList<string> values)
    {
        Console.WriteLine($"{label}:");
        if (values.Count == 0) { Console.WriteLine("  unsupported"); return; }
        foreach (string value in values) Console.WriteLine($"  {value}");
    }

    // Public command implementations below. These are the only two paths a
    // supported end user is expected to exercise.

    /// Runs `drvctl export`. Resolves copy concurrency via CopyWorkerPolicy,
    /// performs the export, prints the friendly/verbose summary, then layers
    /// on --verify/--full-verify/--dism if requested. --dism additionally
    /// requires elevation, checked before the export even starts so a
    /// non-elevated run fails fast instead of after copying gigabytes of driver files.
    private static async Task<int> RunExportAsync(
        IDriverExporter exporter,
        ExportCommandOptions options
    )
    {
        if (
            options.ValidationMode == ExportValidationMode.Dism &&
            !Elevation.IsAdministrator()
        )
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "drvctl export --dism requires an elevated terminal because DISM"
            );
            Console.Error.WriteLine(
                "requires administrative privileges."
            );

            return ExitCodes.RuntimeFailure;
        }

        WorkerSelection copyWorkers =
            CopyWorkerPolicy.Resolve();

        ExportResult result;

        try
        {
            result =
                exporter.Export(
                    new ExportRequest(
                        options.Destination,
                        copyWorkers.Workers,
                        options.Verbose,
                        copyWorkers.FromOverride
                    )
                );
        }
        catch (Exception error)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("drvctl export failed:");
            Console.Error.WriteLine($"  {error.Message}");
            return ExitCodes.RuntimeFailure;
        }

        // Quick confidence is metadata-only, so it reuses the same conservative
        // copy policy. Full/DISM comparisons hash everything and benefit from
        // much higher parallelism, so they get their own automatic strategy.
        WorkerSelection? verificationWorkers =
            options.ValidationMode switch
            {
                ExportValidationMode.None => null,
                ExportValidationMode.Quick => CopyWorkerPolicy.Resolve(),
                ExportValidationMode.Full or ExportValidationMode.Dism => VerificationWorkerPolicy.Resolve(),
                _ => null
            };

        ConsoleOutput.PrintExportSummary(
            result,
            options.Verbose,
            copyWorkers,
            verificationWorkers,
            validationFollows: options.ValidationMode != ExportValidationMode.None
        );

        if (options.Benchmark)
        {
            BenchmarkPrinter.PrintExport(
                result
            );
        }

        switch (options.ValidationMode)
        {
            case ExportValidationMode.None:
                return ExitCodes.Success;

            case ExportValidationMode.Quick:
            case ExportValidationMode.Full:
                return RunSourceVerification(
                    result,
                    options,
                    verificationWorkers!.Value.Workers
                );

            case ExportValidationMode.Dism:
                return await RunDismComparisonAsync(
                    result,
                    options,
                    verificationWorkers!.Value.Workers
                );

            default:
                throw new InvalidOperationException(
                    "Unknown export validation mode."
                );
        }
    }

    /// Backs --verify (Quick) and --full-verify (Full): compares the export
    /// against the Driver Store source it was copied from. Never touches DISM.
    private static int RunSourceVerification(
        ExportResult result,
        ExportCommandOptions options,
        int verificationWorkers
    )
    {
        VerificationDepth depth =
            options.ValidationMode == ExportValidationMode.Full
                ? VerificationDepth.Full
                : VerificationDepth.Quick;

        string modeLabel =
            depth == VerificationDepth.Full
                ? "Expensive confidence"
                : "Quick confidence";

        string? successFlavor =
            depth == VerificationDepth.Full
                ? "Every byte has receipts."
                : null;

        TreeComparisonResult comparison =
            new FileTreeVerifier().CompareToSource(
                result.Destination,
                result.PackageDirectories,
                verificationWorkers,
                depth
            );

        ConsoleOutput.PrintSourceVerificationResult(
            comparison,
            modeLabel,
            successFlavor,
            options.Verbose
        );

        Console.WriteLine();
        Console.WriteLine("Done.");

        return comparison.ExactMatch
            ? ExitCodes.Success
            : ExitCodes.VerificationMismatch;
    }

    /// Backs --dism: creates a temporary DISM reference export, compares it
    /// against the already-completed drvctl export, and cleans it up.
    /// The only path in the whole CLI that calls DISM.
    private static async Task<int> RunDismComparisonAsync(
        ExportResult result,
        ExportCommandOptions options,
        int verificationWorkers
    )
    {
        Console.WriteLine();
        Console.WriteLine("Challenging Windows itself: running a temporary DISM export...");

        DismComparisonRunner runner =
            new(new DismRunner(), new FileTreeVerifier());

        DismComparisonOutcome outcome =
            await runner.RunAsync(
                result.Destination,
                verificationWorkers
            );

        switch (outcome.Status)
        {
            case DismComparisonStatus.NotElevated:
                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    "drvctl export --dism requires an elevated terminal because DISM"
                );
                Console.Error.WriteLine(
                    "requires administrative privileges."
                );
                return ExitCodes.RuntimeFailure;

            case DismComparisonStatus.DismFailed:
                Console.Error.WriteLine();
                Console.Error.WriteLine(outcome.Message);
                if (options.Verbose && outcome.DismError is not null)
                {
                    PrintDismFailureDetails(outcome.DismError);
                }
                return ExitCodes.DismFailure;

            case DismComparisonStatus.Failed:
                Console.Error.WriteLine();
                Console.Error.WriteLine($"drvctl export --dism failed: {outcome.Message}");
                return ExitCodes.RuntimeFailure;
        }

        TreeComparisonResult comparison =
            outcome.Comparison!;

        ConsoleOutput.PrintDismComparisonResult(
            comparison,
            options.Verbose
        );

        if (options.Benchmark)
        {
            BenchmarkPrinter.PrintComparison(
                outcome.DismSeconds,
                result
            );
        }

        Console.WriteLine();
        Console.WriteLine("Done.");

        return comparison.ExactMatch
            ? ExitCodes.Success
            : ExitCodes.VerificationMismatch;
    }

    private static void PrintDismFailureDetails(
        DismException error
    )
    {
        string output =
            string.Join(
                Environment.NewLine,
                new[]
                {
                    error.StandardError.Trim(),
                    error.StandardOutput.Trim()
                }
                .Where(
                    text => !string.IsNullOrWhiteSpace(text)
                )
            );

        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(output);
    }

    private static IDriverExporter CreateExporter()
    {
        return new DriverExporter(
            new DriverStoreResolver(),
            new CopyFile2Engine()
        );
    }

    /// Runs `drvctl list`. Resolves published packages (reusing
    /// CopyWorkerPolicy for the SetupAPI resolution work), applies the
    /// optional provider/class substring filters, then prints the friendly
    /// or --verbose view. Never copies files, calls DISM, or verifies anything.
    private static int RunList(
        DriverStoreResolver resolver,
        ListCommandOptions options
    )
    {
        DriverStoreResolution resolution =
            resolver.Resolve(
                CopyWorkerPolicy.Resolve().Workers,
                includeIdentity: true
            );

        IEnumerable<PublishedDriverPackage> filtered =
            resolution.PublishedPackages;

        if (!string.IsNullOrWhiteSpace(options.ProviderFilter))
        {
            filtered = filtered.Where(
                package =>
                    package.Provider is not null &&
                    package.Provider.Contains(
                        options.ProviderFilter,
                        StringComparison.OrdinalIgnoreCase
                    )
            );
        }

        if (!string.IsNullOrWhiteSpace(options.ClassFilter))
        {
            filtered = filtered.Where(
                package =>
                    package.Class is not null &&
                    package.Class.Contains(
                        options.ClassFilter,
                        StringComparison.OrdinalIgnoreCase
                    )
            );
        }

        PublishedDriverPackage[] packages =
            filtered.ToArray();

        if (packages.Length == 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "No published driver packages matched."
            );

            return ExitCodes.Success;
        }

        if (options.Verbose)
        {
            PrintVerboseList(packages);
        }
        else
        {
            PrintFriendlyList(packages);
        }

        Console.WriteLine();
        Console.WriteLine(
            $"{packages.Length} published driver package" +
            (packages.Length == 1 ? string.Empty : "s")
        );

        return ExitCodes.Success;
    }

    private static void PrintFriendlyList(
        PublishedDriverPackage[] packages
    )
    {
        const string InfHeader = "INF";
        const string ProviderHeader = "Provider";
        const string ClassHeader = "Class";
        const string VersionHeader = "Version";
        const string DateHeader = "Date";
        const string Unknown = "-";

        string[] versions =
            [.. packages.Select(package => package.DriverVersion ?? Unknown)];

        string[] dates =
            [.. packages.Select(package => FormatDriverDate(package.DriverDate))];

        int infWidth =
            Math.Max(InfHeader.Length, packages.Max(package => package.PublishedInfName.Length));

        int providerWidth =
            Math.Max(ProviderHeader.Length, packages.Max(package => (package.Provider ?? Unknown).Length));

        int classWidth =
            Math.Max(ClassHeader.Length, packages.Max(package => (package.Class ?? Unknown).Length));

        int versionWidth =
            Math.Max(VersionHeader.Length, versions.Max(value => value.Length));

        Console.WriteLine();
        Console.WriteLine(
            $"{InfHeader.PadRight(infWidth)}  " +
            $"{ProviderHeader.PadRight(providerWidth)}  " +
            $"{ClassHeader.PadRight(classWidth)}  " +
            $"{VersionHeader.PadRight(versionWidth)}  {DateHeader}"
        );

        for (int index = 0; index < packages.Length; index++)
        {
            PublishedDriverPackage package = packages[index];

            Console.WriteLine(
                $"{package.PublishedInfName.PadRight(infWidth)}  " +
                $"{(package.Provider ?? Unknown).PadRight(providerWidth)}  " +
                $"{(package.Class ?? Unknown).PadRight(classWidth)}  " +
                $"{versions[index].PadRight(versionWidth)}  {dates[index]}"
            );
        }
    }

    private static void PrintVerboseList(
        PublishedDriverPackage[] packages
    )
    {
        const string Unknown = "-";

        foreach (PublishedDriverPackage package in packages)
        {
            Console.WriteLine();
            Console.WriteLine(package.PublishedInfName);
            Console.WriteLine($"  Provider              : {package.Provider ?? Unknown}");
            Console.WriteLine($"  Class                 : {package.Class ?? Unknown}");
            Console.WriteLine($"  ClassGuid             : {package.ClassGuid ?? Unknown}");
            Console.WriteLine($"  Version               : {package.DriverVersion ?? Unknown}");
            Console.WriteLine($"  Date                  : {FormatDriverDate(package.DriverDate)}");
            Console.WriteLine($"  Catalog               : {package.CatalogFile ?? Unknown}");
            Console.WriteLine($"  Driver Store package  : {Path.GetFileName(package.PackageDirectory)}");
            Console.WriteLine($"  Driver Store directory: {package.PackageDirectory}");
            Console.WriteLine($"  Driver Store INF      : {package.StoreInfPath}");
        }
    }

    private static string FormatDriverDate(
        string? rawDate
    )
    {
        if (string.IsNullOrWhiteSpace(rawDate))
        {
            return "-";
        }

        return DateTime.TryParseExact(
            rawDate,
            "MM/dd/yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out DateTime parsed
        )
            ? parsed.ToString("yyyy-MM-dd")
            : rawDate;
    }
}

/// Automatic concurrency for copy-shaped work (export payload copy, SetupAPI
/// package resolution). Kept conservative - more logical CPUs does not mean
/// faster copying, and a small pool was the measured sweet spot.
internal static class CopyWorkerPolicy
{
    private const int MinWorkers = 1;
    private const int MaxWorkers = 4;
    private const int LowCoreDivisor = 2;
    private const string OverrideEnvironmentVariable = "DRVCTL_COPY_WORKERS";

    internal static WorkerSelection Resolve()
    {
        if (WorkerOverride.TryRead(OverrideEnvironmentVariable, out int overridden))
        {
            return new WorkerSelection(overridden, FromOverride: true);
        }

        int logicalCpus =
            Environment.ProcessorCount;

        int workers =
            logicalCpus > MaxWorkers
                ? MaxWorkers
                : Math.Max(MinWorkers, logicalCpus / LowCoreDivisor);

        return new WorkerSelection(workers, FromOverride: false);
    }
}

/// Automatic concurrency for hash-heavy verification (--full-verify, --dism).
/// Hashing scales with cores much better than copying does, so this is
/// deliberately independent of CopyWorkerPolicy.
internal static class VerificationWorkerPolicy
{
    private const int MaxWorkers = 32;
    private const string OverrideEnvironmentVariable = "DRVCTL_VERIFY_WORKERS";

    internal static WorkerSelection Resolve()
    {
        if (WorkerOverride.TryRead(OverrideEnvironmentVariable, out int overridden))
        {
            return new WorkerSelection(overridden, FromOverride: true);
        }

        int workers =
            Math.Min(Environment.ProcessorCount, MaxWorkers);

        return new WorkerSelection(workers, FromOverride: false);
    }
}

/// Not a public CLI surface. An escape hatch for research/benchmark scripts
/// that need to force a worker count. Normal users never see or set these.
internal static class WorkerOverride
{
    internal static bool TryRead(
        string variableName,
        out int workers
    )
    {
        string? raw =
            Environment.GetEnvironmentVariable(variableName);

        if (
            !string.IsNullOrWhiteSpace(raw) &&
            int.TryParse(raw, out int parsed) &&
            parsed >= 1
        )
        {
            workers = parsed;
            return true;
        }

        workers = 0;
        return false;
    }
}
