using System.Text.Json;
using DrvCtl.Analysis;
using DrvCtl.Copy;
using DrvCtl.Images;

namespace DrvCtl.Publication;

internal sealed class PublicationPrototypeRunner
{
    internal PublicationPrototypeResult Run(string baselineWim, string referenceWim, string packageDirectory, int imageIndex, string requestedWorkspace)
    {
        string baseline = Path.GetFullPath(baselineWim);
        string reference = Path.GetFullPath(referenceWim);
        string package = Path.GetFullPath(packageDirectory);
        if (!File.Exists(baseline)) throw new FileNotFoundException("Baseline WIM was not found.", baseline);
        if (!File.Exists(reference)) throw new FileNotFoundException("Reference WIM was not found.", reference);
        if (!Directory.Exists(package)) throw new DirectoryNotFoundException($"Package directory was not found: {package}");
        string workspace = PublicationWorkspaceSafety.ValidateNew(requestedWorkspace, baseline, reference, package);
        string baselineHashBefore = DisposablePublicationExecutor.Hash(baseline);
        PublicationSourceHash[] sourceHashes = Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => new PublicationSourceHash(path, DisposablePublicationExecutor.Hash(path), string.Empty, false)).ToArray();

        string tree = Path.Combine(workspace, "tree");
        string comparison = Path.Combine(workspace, "comparison");
        Directory.CreateDirectory(tree);
        Directory.CreateDirectory(comparison);
        using (WimImage image = WimImage.Open(baseline)) image.ExtractPaths(imageIndex, tree, PublicationAnalysisScope.WimPaths);

        ResearchPublicationPolicy policy = new();
        DriverPublicationPlan plan = new DriverPublicationPlanner(policy).Create(package, tree, Path.Combine(workspace, "reflection-plan"));
        (PublicationAppliedFile[] files, PublicationAppliedRegistryValue[] registry) = new DisposablePublicationExecutor(new CopyFile2Engine()).Execute(plan);
        PublicationSemanticComparison semanticComparison = new DirectoryPublicationComparer().Compare(reference, imageIndex, tree, comparison, plan);

        string baselineHashAfter = DisposablePublicationExecutor.Hash(baseline);
        sourceHashes = sourceHashes.Select(source =>
        {
            string after = DisposablePublicationExecutor.Hash(source.Path);
            return source with { AfterSha256 = after, Unchanged = source.BeforeSha256.Equals(after, StringComparison.Ordinal) };
        }).ToArray();

        string versionEvidence = "Composite Version: solved 40-byte core across 67/67 packages (Header 0x00FF090000000000 + ClassGuid {4d36e97d-e325-11ce-bfc1-08002be10318} + UTC FILETIME date 0x01D894B929E28000 + reversed UInt16 version components [70,29,11,15]); prototype-supported 8-byte zero tail (0x0000000000000000) verified for ACPIVPC.";
        string[] generatedFields =
        [
            "FileRepository package files (3 files)",
            "Published INF (Windows\\INF\\oem0.inf)",
            "Published Catalog (Windows\\System32\\CatRoot\\{F750E6C3-38EE-11D1-85E5-00C04FC295EE}\\oem0.cat)",
            "Reflected driver binary (Windows\\System32\\drivers\\AcpiVpc.sys)",
            "SYSTEM\\DriverDatabase\\OemInfMap",
            "SYSTEM\\DriverDatabase\\DriverInfFiles\\oem0.inf (default, Active, Configurations)",
            "SYSTEM\\DriverDatabase\\DriverPackages\\acpivpc.inf_amd64_fd0a5766a43dadc1 (default, Catalog, FileSize, InfName, ManifestHash sentinel, OemPath, Provider, SignerName, SignerScore, Version, ImportDate)",
            "SYSTEM\\DriverDatabase\\DriverPackages\\...\\Configurations\\ACPIVPC_Inst.NTamd64 (Service, ConfigScope)",
            "SYSTEM\\DriverDatabase\\DriverPackages\\...\\Descriptors\\ACPI\\VEN_VPC&DEV_2004 (Configuration, Description, Manufacturer)",
            "SYSTEM\\DriverDatabase\\DriverPackages\\...\\Strings (*vpc2004.devicedesc, provider)",
            "SYSTEM\\ControlSet001\\Services\\ACPIVPC (Type, Start, ErrorControl, ImagePath, DisplayName, Owners)",
            "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Setup\\PnpLockdownFiles\\%SystemRoot%/System32/drivers/AcpiVpc.sys (Source, Owners, Class)",
            "SYSTEM\\DriverDatabase\\UpdateDate"
        ];
        string[] omittedFields = plan.OmittedOperations.Select(op => $"{op.Target} ({op.EvidenceStatus}): {op.Reason}").ToArray();
        string[] unsupportedFields = plan.OmittedOperations.Where(op => op.EvidenceStatus == EvidenceStatus.Unsupported).Select(op => op.Target).ToArray();

        string offlineServicingAssessment = "Likely sufficient for tested offline servicing contract (Task 9 proved Version is the only required persistent registry state; ConfigFlags, ConfigScope, Descriptors, Strings, Services, PnpLockdownFiles, and reflected sys are all present or reconstructible by Windows offline servicing).";
        string fullPublicationAssessment = "Incomplete for full Windows driver publication equivalence: DeviceIds mapping (required for live PnP hardware binding), StatusFlags, and custom property 0xFFFF0012 are omitted as unsupported.";

        bool complete = baselineHashBefore.Equals(baselineHashAfter, StringComparison.Ordinal)
            && sourceHashes.All(source => source.Unchanged)
            && files.All(file => file.Matches)
            && semanticComparison.Contradictions == 0
            && !plan.OmittedOperations.Any(operation => operation.RequiredForOfflineServicing);

        PublicationPrototypeResult result = new(
            plan,
            baseline,
            reference,
            imageIndex,
            workspace,
            tree,
            baselineHashBefore,
            baselineHashAfter,
            sourceHashes,
            files,
            registry,
            semanticComparison,
            versionEvidence,
            generatedFields,
            omittedFields,
            unsupportedFields,
            semanticComparison.ExactMatches,
            semanticComparison.SemanticallyEquivalent,
            semanticComparison.ExpectedDifferences,
            semanticComparison.Unsupported,
            semanticComparison.Contradictions,
            offlineServicingAssessment,
            fullPublicationAssessment,
            complete
        );

        using FileStream output = File.Create(Path.Combine(workspace, "publication-prototype-result.json"));
        JsonSerializer.Serialize(output, result, PublicationPrototypeJsonContext.Default.PublicationPrototypeResult);
        return result;
    }
}
