using System.Buffers.Binary;
using System.Text;
using DrvCtl.Drivers;
using DrvCtl.Native;
using DrvCtl.Offline;
using DrvCtl.Research.Task8;

namespace DrvCtl.Publication;

internal sealed class DriverPublicationPlanner(ResearchPublicationPolicy policy)
{
    private const uint RegSz = 1;
    private const uint RegExpandSz = 2;
    private const uint RegBinary = 3;
    private const uint RegDword = 4;
    private const uint RegMultiSz = 7;
    private const uint RegQword = 11;
    private const ulong PrototypeManifestHashSentinel = ulong.MaxValue;
    private const string CatalogRoot = @"Windows\System32\CatRoot\{F750E6C3-38EE-11D1-85E5-00C04FC295EE}";

    internal DriverPublicationPlan Create(string packageDirectory, string treeRoot, string operationWorkspace)
    {
        string repository = policy.ValidateRepositoryIdentity(packageDirectory);
        DriverStagingPlan staging = new DriverStagingPlanner().Create(packageDirectory);
        string databaseHive = policy.SelectDriverDatabaseHive(staging.Package.Class);
        string publishedInf = policy.AllocateOemInf(treeRoot, repository);
        int publishedIndex = policy.ParseOemIndex(publishedInf);
        string publishedCatalog = $"oem{publishedIndex}.cat";
        Dictionary<string, StoreFilePlan> files = staging.StoreFiles.ToDictionary(file => file.FileName, StringComparer.OrdinalIgnoreCase);
        StoreFilePlan sourceInf = files[staging.Package.Inf];
        StoreFilePlan? sourceCatalog = staging.Package.Catalog is null ? null : files[staging.Package.Catalog];

        InfInspection inspection = new InfInspector().Inspect(sourceInf.SourcePath);

        List<PublicationFileCopy> fileOperations = staging.StoreFiles.Select(file => new PublicationFileCopy(
            file.SourcePath,
            Path.Combine(@"Windows\System32\DriverStore\FileRepository", repository, file.FileName),
            "FileRepository byte-for-byte publication")).ToList();
        fileOperations.Add(new PublicationFileCopy(sourceInf.SourcePath, Path.Combine(@"Windows\INF", publishedInf), "Published INF byte-for-byte copy"));
        if (sourceCatalog is not null)
            fileOperations.Add(new PublicationFileCopy(sourceCatalog.SourcePath, Path.Combine(CatalogRoot, publishedCatalog), "Observed offline CatRoot file publication"));

        string systemHive = Path.Combine(treeRoot, @"Windows\System32\config\SYSTEM");
        string softwareHive = Path.Combine(treeRoot, @"Windows\System32\config\SOFTWARE");
        string driversHive = Path.Combine(treeRoot, @"Windows\System32\config\DRIVERS");
        OfflineApplyPlan reflection = new OfflineApplyPlanner().Create(packageDirectory, operationWorkspace, systemHive, softwareHive, driversHive);
        fileOperations.AddRange(reflection.FileOperations.Select(copy => new PublicationFileCopy(copy.SourcePath, copy.DestinationRelativePath, "Validated OfflineApplyPlan reflection")));

        List<PublicationRegistryValue> registry = [];
        int[] occupied = Directory.EnumerateFiles(Path.Combine(treeRoot, "Windows", "INF"), "oem*.inf")
            .Select(path => Path.GetFileName(path)).Select(policy.ParseOemIndex).Append(publishedIndex).Distinct().Order().ToArray();
        Add(registry, "SYSTEM", "DriverDatabase", "OemInfMap", RegBinary, policy.EncodeOemInfMap(occupied), "Task 6 research occupancy bitmap: index n maps to bit 7-(n mod 8) in byte n/8", EvidenceStatus.Solved, "Cross-validated against five Task 6 states", "Stable for a given occupied set", false, false);

        string infFileKey = $@"DriverDatabase\DriverInfFiles\{publishedInf}";
        Add(registry, databaseHive, infFileKey, "", RegMultiSz, MultiString(repository), "Repository identity supplied by exported package directory", EvidenceStatus.Solved, "High within prototype domain", "Stable", false, false);
        Add(registry, databaseHive, infFileKey, "Active", RegSz, String(repository), "Active repository identity equals the independently supplied repository directory", EvidenceStatus.Solved, "Observed across Task 5/6 specimens", "Stable", false, false);
        if (staging.Package.InstallSections.Length > 0)
            Add(registry, databaseHive, infFileKey, "Configurations", RegMultiSz, MultiString(staging.Package.InstallSections), "SetupAPI-selected AMD64 install sections", EvidenceStatus.Solved, "High for inspected INF sections", "Stable", false, false);

        string packageKey = $@"DriverDatabase\DriverPackages\{repository}";
        Add(registry, databaseHive, packageKey, "", RegSz, String(publishedInf), "Research OEM allocation", EvidenceStatus.Solved, "Provisional Task 6 policy", "OEM-identity-dependent", false, false);
        if (staging.Package.Catalog is not null) Add(registry, databaseHive, packageKey, "Catalog", RegSz, String(staging.Package.Catalog), "INF CatalogFile through SetupAPI", EvidenceStatus.Solved, "High", "Stable", false, false);
        Add(registry, databaseHive, packageKey, "FileSize", RegQword, Qword(staging.StoreFiles.Sum(file => new FileInfo(file.SourcePath).Length)), "Sum of resolved package file lengths; equals ACPIVPC observed 61,580 bytes", EvidenceStatus.Solved, "Validated for ACPIVPC package", "Stable", false, false);
        Add(registry, databaseHive, packageKey, "InfName", RegSz, String(staging.Package.Inf), "Source INF filename", EvidenceStatus.Solved, "High", "Stable", false, false);
        Add(registry, databaseHive, packageKey, "ManifestHash", RegQword, Qword(PrototypeManifestHashSentinel), "Prototype sentinel observed for all seven Task 6 specimens", EvidenceStatus.PrototypeSupported, "Observed invariant; meaning not claimed universal", "Stable in observed domain", false, false);
        Add(registry, databaseHive, packageKey, "OemPath", RegSz, String(Path.GetFullPath(packageDirectory)), "Supplied exported package path", EvidenceStatus.PrototypeSupported, "Matches Task 5/6 representation", "Host-path-dependent", false, false);
        if (staging.Package.Provider is not null) Add(registry, databaseHive, packageKey, "Provider", RegSz, String(staging.Package.Provider), "INF Provider expanded by SetupAPI", EvidenceStatus.Solved, "High", "Stable", false, false);

        // SignerName and SignerScore from SetupVerifyInfFileW
        if (inspection.Signature?.SignerName is not null)
            Add(registry, databaseHive, packageKey, "SignerName", RegSz, String(inspection.Signature.SignerName), "Signer identity derived via SetupVerifyInfFileW", EvidenceStatus.Solved, "Exact match across 66/66 packages storing SignerName", "Stable", false, false);
        if (inspection.Signature is not null)
            Add(registry, databaseHive, packageKey, "SignerScore", RegDword, Dword((int)inspection.Signature.SignerScore), "Signer score derived via SetupVerifyInfFileW", EvidenceStatus.Solved, "Exact match across 67/67 packages", "Stable", false, false);

        // Version encoding (40 bytes core solved + 8 bytes zero tail prototype supported for ACPIVPC)
        if (staging.Package.ClassGuid is not null && staging.Package.DriverVersion is not null)
        {
            byte[] coreVersion = DriverVersionValuePredictor.PredictCoreValue(staging.Package.ClassGuid, staging.Package.DriverVersion);
            byte[] fullVersion = new byte[48];
            coreVersion.CopyTo(fullVersion, 0); // Tail bytes remain 0x00 (observed zero tail for ACPIVPC)
            Add(registry, databaseHive, packageKey, "Version", RegBinary, fullVersion, "Composite Version: solved 40-byte core (Header+ClassGuid+Date+DottedVer) across 67/67 packages; prototype-supported zero tail validated for ACPIVPC", EvidenceStatus.PrototypeSupported, "Exact match for ACPIVPC specimen", "Stable", true, false);
        }

        // Configurations: Service and ConfigScope
        foreach (ReflectedService service in staging.Reflection.Services)
        {
            string configurationKey = $@"{packageKey}\Configurations\{service.InstallSection}";
            Add(registry, databaseHive, configurationKey, "Service", RegSz, String(service.Name), "AddService selected by SetupAPI", EvidenceStatus.Solved, "High", "Stable", false, false);
            // ConfigScope: prototype constant validated across 105/105 configurations
            Add(registry, databaseHive, configurationKey, "ConfigScope", RegDword, Dword(0x00000F7F), "Configuration scope prototype constant validated across 105/105 observed configurations", EvidenceStatus.PrototypeSupported, "Validated across 105/105 configurations; bit meanings unresolved", "Stable in observed domain", false, false);
        }

        // Descriptors and Strings
        HashSet<string> requiredStrings = new(StringComparer.OrdinalIgnoreCase);
        DescriptorPrediction[] descriptorPredictions = DescriptorPredictor.Predict(inspection);
        foreach (DescriptorPrediction prediction in descriptorPredictions)
        {
            string descriptorKey = $@"{packageKey}\Descriptors\{prediction.Id}";
            Add(registry, databaseHive, descriptorKey, "Configuration", RegSz, String(prediction.Configuration), "Descriptor Configuration derived from INF model install section", EvidenceStatus.Solved, "Exact match for ACPIVPC model", "Stable", false, false);
            Add(registry, databaseHive, descriptorKey, "Description", RegSz, String(prediction.Description), "Descriptor Description token derived from INF model description", EvidenceStatus.Solved, "Exact match for ACPIVPC model", "Stable", false, false);
            Add(registry, databaseHive, descriptorKey, "Manufacturer", RegSz, String(prediction.Manufacturer), "Descriptor Manufacturer token derived from INF model manufacturer", EvidenceStatus.Solved, "Exact match for ACPIVPC model", "Stable", false, false);
            foreach (string token in prediction.RequiredStrings) requiredStrings.Add(token);
        }

        foreach (string token in requiredStrings)
        {
            InfStringValue? match = inspection.Strings.FirstOrDefault(str => str.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                Add(registry, databaseHive, packageKey + @"\Strings", match.Name.ToLowerInvariant(), RegSz, String(match.Value), "INF string token expansion required by Descriptors", EvidenceStatus.Solved, "High", "Stable", false, false);
        }

        // Service Reflection: Type, Start, ErrorControl, ImagePath, DisplayName, Owners
        foreach (OfflineRegistryValueSet value in reflection.RegistryValues)
        {
            byte[] encoded = value.Type == OfflineRegistryValueType.Dword ? Dword(value.DwordValue!.Value) : String(value.StringValue!);
            uint registryType = value.Type == OfflineRegistryValueType.Dword ? RegDword : value.Name.Equals("ImagePath", StringComparison.OrdinalIgnoreCase) ? RegExpandSz : RegSz;
            Add(registry, value.Hive, value.KeyPath, value.Name, registryType, encoded, "Validated OfflineApplyPlan service reflection; service ImagePath uses REG_EXPAND_SZ", EvidenceStatus.Solved, "Validated against ACPIVPC fixture", "Stable", false, false);
        }

        ServiceMetadataPrediction[] serviceMetadata = ServiceMetadataPredictor.Predict(inspection, publishedInf);
        foreach (ServiceMetadataPrediction meta in serviceMetadata)
        {
            string serviceKey = $@"ControlSet001\Services\{meta.ServiceName}";
            Add(registry, "SYSTEM", serviceKey, "DisplayName", RegSz, String(meta.DisplayName), "Service DisplayName formatted with published OEM INF, string token, and expanded description", EvidenceStatus.PrototypeSupported, "Validated for ACPIVPC dedicated service", "Stable", false, false);
            Add(registry, "SYSTEM", serviceKey, "Owners", RegMultiSz, MultiString(meta.Owners), "Dedicated service ownership list containing published OEM INF", EvidenceStatus.Solved, "Matches 14/14 dedicated services in Task 8", "Stable", false, false);
        }

        // PnpLockdownFiles
        foreach (ReflectedFileCopy copy in staging.Reflection.Copies)
        {
            PnpLockdownPrediction pnp = PnpLockdownPredictor.Predict(copy.DestinationPath, repository, Path.GetFileName(copy.DestinationPath), publishedInf, inspection.PnpLockdown);
            string pnpKey = $@"Microsoft\Windows\CurrentVersion\Setup\PnpLockdownFiles\{pnp.DestinationKey}";
            Add(registry, "SOFTWARE", pnpKey, "Source", RegExpandSz, String(pnp.Source), "PnpLockdown FileRepository source path using REG_EXPAND_SZ", EvidenceStatus.Solved, "Matches 13/13 observations in Task 8", "Stable", false, false);
            Add(registry, "SOFTWARE", pnpKey, "Owners", RegMultiSz, MultiString(pnp.Owners), "PnpLockdown single-owner registration list", EvidenceStatus.Solved, "Matches 13/13 single-owner observations in Task 8", "Stable", false, false);
            Add(registry, "SOFTWARE", pnpKey, "Class", RegDword, Dword(pnp.Class), "PnpLockdown Class (5 for absent PnpLockdown directive in INF)", EvidenceStatus.PrototypeSupported, "Prototype-supported by 2/2 observed PnpLockdown-absent records", "Stable in observed domain", false, false);
        }

        long now = DateTime.UtcNow.ToFileTimeUtc();
        Add(registry, databaseHive, packageKey, "ImportDate", RegBinary, Qword(now), "Current UTC FILETIME; exact value intentionally does not target reference", EvidenceStatus.Solved, "Representation validated by Task 6 variability", "Volatile", false, false);
        Add(registry, databaseHive, "DriverDatabase", "UpdateDate", RegBinary, Qword(now), "Current UTC FILETIME; exact value intentionally does not target reference", EvidenceStatus.Solved, "Representation validated by Task 6 variability", "Volatile", false, false);

        PublicationOmittedOperation[] omitted =
        [
            new("DeviceIds mapping", "General encoder unresolved (859 counterexamples in Task 8); 01FF0000 fixture bytes are not replayed. Task 9 proved optional for offline servicing; boot/PnP matching unproven.", EvidenceStatus.Unsupported, false, true),
            new("DeviceIds Class index", "DeviceIds class GUID index mapping ({4d36e97d-e325-11ce-bfc1-08002be10318}) encoding is unresolved.", EvidenceStatus.Unsupported, false, true),
            new("StatusFlags", "18 distinct observed values with no deterministic rule; omitted as unsupported. Task 9 proved optional for offline servicing.", EvidenceStatus.Unsupported, false, false),
            new("Configurations ConfigFlags", "General derivation rule not established; omitted as unsupported. Task 9 proved reconstructed by Windows offline servicing.", EvidenceStatus.Unsupported, false, false),
            new("Custom property 0xFFFF0012", "Internal non-standard property {4da162c1-5eb1-4140-a444-5064c9814e76}\\0009; origin undocumented and omitted from prototype. Task 9 proved optional for offline servicing.", EvidenceStatus.Unsupported, false, false),
            new("setupapi.offline.log", "Servicing log deliberately not synthesized by prototype.", EvidenceStatus.OmittedByPolicy, false, false),
            new("CatRoot2 database state", "Live catalog database mutation outside CatRoot file is not performed.", EvidenceStatus.OmittedByPolicy, false, false)
        ];

        if (policy.OemInfMapValidation.Any(validation => !validation.Matches))
            throw new InvalidOperationException("Research OemInfMap encoding failed its Task 6 observations.");
        return new DriverPublicationPlan(staging, reflection, treeRoot, repository, "Supplied exported-package directory name", "unsupported", publishedInf, publishedCatalog, "Task 6 research hypothesis: reuse existing identity, otherwise lowest unused non-negative OEM index", databaseHive, [.. fileOperations], [.. registry], [new(databaseHive, packageKey, "ImportDate", "Generated current UTC FILETIME"), new(databaseHive, "DriverDatabase", "UpdateDate", "Generated current UTC FILETIME")], omitted, policy.OemInfMapValidation);
    }

    private static void Add(List<PublicationRegistryValue> values, string hive, string key, string name, uint type, byte[] data, string derivation, EvidenceStatus status, string confidence, string volatility, bool reqServicing, bool reqPnP)
    {
        if (status == EvidenceStatus.Unsupported)
            throw new InvalidOperationException($"Attempted to add unsupported registry value to plan: {hive}\\{key}\\{name}");
        values.Add(new(hive, key, name, type, data, derivation, status, confidence, volatility, reqServicing, reqPnP));
    }
    private static byte[] String(string value) => Encoding.Unicode.GetBytes(value + '\0');
    private static byte[] MultiString(params string[] values) => Encoding.Unicode.GetBytes(string.Join('\0', values) + "\0\0");
    private static byte[] Dword(int value) { byte[] data = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(data, value); return data; }
    private static byte[] Qword(long value) { byte[] data = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(data, value); return data; }
    private static byte[] Qword(ulong value) { byte[] data = new byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(data, value); return data; }
}
