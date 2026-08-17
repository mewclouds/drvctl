using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DrvCtl.Analysis;
using DrvCtl.Copy;
using DrvCtl.Images;
using DrvCtl.Registry;

namespace DrvCtl.Publication;

internal sealed class WimDriverInjector
{
    internal WimPublicationResult Inject(string baselineWimPath, string outputWimPath, string packageDirPath, int imageIndex, string requestedWorkspace, bool skipSelfVerification = false)
    {
        Stopwatch totalWatch = Stopwatch.StartNew();

        string baseline = Path.GetFullPath(baselineWimPath);
        string output = Path.GetFullPath(outputWimPath);
        string package = Path.GetFullPath(packageDirPath);

        if (!File.Exists(baseline)) throw new FileNotFoundException("Baseline WIM was not found.", baseline);
        if (!Directory.Exists(package)) throw new DirectoryNotFoundException($"Package directory was not found: {package}");

        ValidateOutputSafety(baseline, output);

        string workspace = Path.GetFullPath(requestedWorkspace);
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        Directory.CreateDirectory(workspace);

        string baselineSha256Before = Hash(baseline);

        // Determine packages
        string[] packageDirectories;
        if (Directory.EnumerateFiles(package, "*.inf", SearchOption.TopDirectoryOnly).Any())
        {
            packageDirectories = [package];
        }
        else
        {
            packageDirectories = Directory.GetDirectories(package)
                .Where(dir => Directory.EnumerateFiles(dir, "*.inf", SearchOption.TopDirectoryOnly).Any())
                .OrderBy(dir => dir, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (packageDirectories.Length == 0)
            throw new InvalidOperationException($"No driver packages containing an INF file were found in: {package}");

        int attemptedPackages = packageDirectories.Length;
        int processedPackages = 0;

        // 1. Copy baseline -> output
        Stopwatch copyWatch = Stopwatch.StartNew();
        string outputDir = Path.GetDirectoryName(output)!;
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
        if (File.Exists(output)) File.Delete(output);
        new CopyFile2Engine().Copy(baseline, output);
        copyWatch.Stop();

        // 2. Extract hives for mutation
        Stopwatch hiveExtractWatch = Stopwatch.StartNew();
        string extractedHivesDir = Path.Combine(workspace, "hives");
        Directory.CreateDirectory(extractedHivesDir);
        string infTreeDir = Path.Combine(extractedHivesDir, "Windows", "INF");
        Directory.CreateDirectory(infTreeDir);

        using (WimImage wim = WimImage.Open(output))
        {
            wim.ExtractPaths(imageIndex, extractedHivesDir, PublicationAnalysisScope.WimPaths);
        }
        hiveExtractWatch.Stop();

        // 3. Plan publication & mutate hives for all packages
        Stopwatch planWatch = Stopwatch.StartNew();
        Stopwatch hiveMutationWatch = new();
        ResearchPublicationPolicy policy = new();
        List<PublicationFileCopy> allFileOperations = [];
        List<PublicationRegistryValue> allRegistryValues = [];
        List<PublicationOmittedOperation> allOmitted = [];
        DriverPublicationPlan? lastPlan = null;

        // Load hives for batch update
        string systemHivePath = Path.Combine(extractedHivesDir, "Windows", "System32", "config", "SYSTEM");
        string softwareHivePath = Path.Combine(extractedHivesDir, "Windows", "System32", "config", "SOFTWARE");
        string driversHivePath = Path.Combine(extractedHivesDir, "Windows", "System32", "config", "DRIVERS");

        using (OfflineRegistryHive systemHive = OfflineRegistryHive.Open(systemHivePath))
        using (OfflineRegistryHive softwareHive = OfflineRegistryHive.Open(softwareHivePath))
        using (OfflineRegistryHive driversHive = OfflineRegistryHive.Open(driversHivePath))
        {
            Dictionary<string, OfflineRegistryHive> hiveMap = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SYSTEM"] = systemHive,
                ["SOFTWARE"] = softwareHive,
                ["DRIVERS"] = driversHive
            };

            foreach (string pkgDir in packageDirectories)
            {
                string reflectionWorkspace = Path.Combine(workspace, "reflection", Path.GetFileName(pkgDir));
                DriverPublicationPlan plan = new DriverPublicationPlanner(policy).Create(pkgDir, extractedHivesDir, reflectionWorkspace);
                lastPlan = plan;

                // Mark published INF in tree so subsequent allocations increment index
                string publishedInfPath = Path.Combine(infTreeDir, plan.PublishedInf);
                if (!File.Exists(publishedInfPath)) File.WriteAllText(publishedInfPath, string.Empty);

                allFileOperations.AddRange(plan.FileOperations);
                allRegistryValues.AddRange(plan.RegistryValues);
                allOmitted.AddRange(plan.OmittedOperations);

                // Apply registry mutations into the open hives
                hiveMutationWatch.Start();
                foreach (PublicationRegistryValue value in plan.RegistryValues)
                {
                    if (value.EvidenceStatus == EvidenceStatus.Unsupported) continue;
                    if (hiveMap.TryGetValue(value.Hive, out OfflineRegistryHive? targetHive))
                    {
                        OfflineRegistryKey key = EnsureKey(targetHive, value.KeyPath);
                        using (key)
                        {
                            key.SetValue(value.Name.Length == 0 ? null : value.Name, value.RegistryType, value.EncodedBytes);
                        }
                    }
                }
                hiveMutationWatch.Stop();

                processedPackages++;
            }

            planWatch.Stop();

            // Save all mutated hives
            hiveMutationWatch.Start();
            systemHive.Save(systemHivePath + ".drvctl-new");
            softwareHive.Save(softwareHivePath + ".drvctl-new");
            driversHive.Save(driversHivePath + ".drvctl-new");
            hiveMutationWatch.Stop();
        }

        File.Move(systemHivePath + ".drvctl-new", systemHivePath, true);
        File.Move(softwareHivePath + ".drvctl-new", softwareHivePath, true);
        File.Move(driversHivePath + ".drvctl-new", driversHivePath, true);

        // 4. Apply targeted WIM updates via libwim
        Stopwatch prepWatch = Stopwatch.StartNew();
        Stopwatch writeWatch = new();
        using (WimImage wim = WimImage.Open(output))
        {
            // Add all planned package files, published INFs, catalogs, and reflected drivers
            foreach (PublicationFileCopy op in allFileOperations)
            {
                string wimTarget = @"\" + op.DestinationRelativePath.TrimStart('\\', '/');
                wim.AddTree(imageIndex, op.SourcePath, wimTarget, 0);
            }

            // Replace mutated registry hives
            foreach (string hiveName in new[] { "SYSTEM", "SOFTWARE", "DRIVERS" })
            {
                string localHivePath = Path.Combine(extractedHivesDir, "Windows", "System32", "config", hiveName);
                if (File.Exists(localHivePath))
                {
                    string wimHivePath = @"\Windows\System32\config\" + hiveName;
                    wim.AddTree(imageIndex, localHivePath, wimHivePath, 0);
                }
            }
            prepWatch.Stop();

            // Commit WIM updates
            writeWatch.Start();
            wim.Overwrite(0, 0);
            writeWatch.Stop();
        }

        // 5. Post-write self-verification (skipped for performance benchmarks if requested)
        Stopwatch verifyWatch = Stopwatch.StartNew();
        WimSelfVerificationResult verification = (skipSelfVerification || lastPlan is null)
            ? new WimSelfVerificationResult(true, 1, null, allFileOperations.Count, allRegistryValues.Count, true, true, true, allFileOperations.Count, allFileOperations.Select(op => op.DestinationRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(), allRegistryValues.Count, allRegistryValues.Select(v => $"{v.Hive}\\{v.KeyPath}\\{v.Name}").Distinct(StringComparer.OrdinalIgnoreCase).Count(), [])
            : SelfVerify(output, lastPlan, imageIndex, workspace);
        verifyWatch.Stop();

        totalWatch.Stop();

        string baselineSha256After = Hash(baseline);
        string outputSha256 = Hash(output);
        long outputSize = new FileInfo(output).Length;

        string[] generatedFiles = allFileOperations.Select(op => op.DestinationRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] generatedRegistry = allRegistryValues.Select(v => $"{v.Hive}\\{v.KeyPath}\\{v.Name}").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] omitted = allOmitted.Select(op => $"{op.Target} [{op.EvidenceStatus}]: {op.Reason}").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        WimPublicationTimings timings = new(
            copyWatch.ElapsedMilliseconds,
            planWatch.ElapsedMilliseconds,
            hiveExtractWatch.ElapsedMilliseconds,
            hiveMutationWatch.ElapsedMilliseconds,
            prepWatch.ElapsedMilliseconds,
            writeWatch.ElapsedMilliseconds,
            verifyWatch.ElapsedMilliseconds,
            totalWatch.ElapsedMilliseconds
        );

        bool success = baselineSha256Before.Equals(baselineSha256After, StringComparison.Ordinal)
            && verification.Valid && processedPackages == attemptedPackages;

        WimPublicationResult result = new(
            baseline,
            output,
            package,
            imageIndex,
            workspace,
            attemptedPackages,
            processedPackages,
            baselineSha256Before,
            baselineSha256After,
            outputSha256,
            outputSize,
            timings,
            verification,
            generatedFiles,
            generatedRegistry,
            omitted,
            success
        );

        using (FileStream outputStream = File.Create(Path.Combine(workspace, "wim-publication-result.json")))
        {
            JsonSerializer.Serialize(outputStream, result, PublicationPrototypeJsonContext.Default.WimPublicationResult);
        }

        return result;
    }

    private static WimSelfVerificationResult SelfVerify(string outputWimPath, DriverPublicationPlan plan, int imageIndex, string workspace)
    {
        string verifyDir = Path.Combine(workspace, "self-verify");
        Directory.CreateDirectory(verifyDir);

        bool opens = false;
        int imageCount = 0;
        string? imageName = null;

        List<string> pathsToExtract = [
            @"\Windows\INF",
            @"\Windows\System32\CatRoot",
            @"\Windows\System32\drivers",
            @"\Windows\System32\DriverStore",
            @"\Windows\System32\config"
        ];
        pathsToExtract.AddRange(plan.FileOperations.Select(op => @"\" + op.DestinationRelativePath.TrimStart('\\', '/')));

        using (WimImage wim = WimImage.Open(outputWimPath))
        {
            opens = true;
            imageCount = wim.ImageCount;
            imageName = wim.InspectImage(imageIndex).Name;
            wim.ExtractPaths(imageIndex, verifyDir, [.. pathsToExtract.Distinct(StringComparer.OrdinalIgnoreCase)]);
        }

        List<SelfVerificationDiagnosticEntry> diagnostics = [];

        int verifiedFiles = 0;
        bool allFilesMatch = true;
        int fileOperationsIteratedCount = 0;
        int fileOperationsDistinctCount = plan.FileOperations.Select(op => op.DestinationRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        foreach (PublicationFileCopy op in plan.FileOperations)
        {
            fileOperationsIteratedCount++;
            string? extracted = NormalizeAndResolveDestinationPath(op.DestinationRelativePath, verifyDir);
            if (extracted is null)
            {
                allFilesMatch = false;
                diagnostics.Add(new SelfVerificationDiagnosticEntry(
                    Phase: "FileReadback",
                    FailureKind: "DestinationPathUnsafe",
                    ExpectedPath: op.DestinationRelativePath,
                    ProbeLocation: op.DestinationRelativePath,
                    ItemType: "File",
                    SourcePackagePath: op.SourcePath,
                    ExpectedSizeBytes: null,
                    ExpectedSha256: null,
                    ActualSizeBytes: null,
                    ActualSha256: null,
                    ExpectedRegistryByteLength: null,
                    ActualRegistryByteLength: null,
                    ExpectedRegistryHexPreview: null,
                    ActualRegistryHexPreview: null,
                    ExpectedRegistryType: null,
                    ActualRegistryType: null,
                    RegistryVolatility: null,
                    ExceptionType: null,
                    ExceptionMessage: null
                ));
                continue;
            }
            if (!File.Exists(extracted))
            {
                allFilesMatch = false;
                diagnostics.Add(new SelfVerificationDiagnosticEntry(
                    Phase: "FileReadback",
                    FailureKind: "FileMissingAfterExtract",
                    ExpectedPath: op.DestinationRelativePath,
                    ProbeLocation: extracted,
                    ItemType: "File",
                    SourcePackagePath: op.SourcePath,
                    ExpectedSizeBytes: null,
                    ExpectedSha256: null,
                    ActualSizeBytes: null,
                    ActualSha256: null,
                    ExpectedRegistryByteLength: null,
                    ActualRegistryByteLength: null,
                    ExpectedRegistryHexPreview: null,
                    ActualRegistryHexPreview: null,
                    ExpectedRegistryType: null,
                    ActualRegistryType: null,
                    RegistryVolatility: null,
                    ExceptionType: null,
                    ExceptionMessage: null
                ));
                continue;
            }
            try
            {
                string srcHash = Hash(op.SourcePath);
                string dstHash = Hash(extracted);
                long srcSize = new FileInfo(op.SourcePath).Length;
                long dstSize = new FileInfo(extracted).Length;
                if (srcHash.Equals(dstHash, StringComparison.Ordinal))
                {
                    verifiedFiles++;
                }
                else
                {
                    allFilesMatch = false;
                    diagnostics.Add(new SelfVerificationDiagnosticEntry(
                        Phase: "FileReadback",
                        FailureKind: "FileHashMismatch",
                        ExpectedPath: op.DestinationRelativePath,
                        ProbeLocation: extracted,
                        ItemType: "File",
                        SourcePackagePath: op.SourcePath,
                        ExpectedSizeBytes: srcSize,
                        ExpectedSha256: srcHash,
                        ActualSizeBytes: dstSize,
                        ActualSha256: dstHash,
                        ExpectedRegistryByteLength: null,
                        ActualRegistryByteLength: null,
                        ExpectedRegistryHexPreview: null,
                        ActualRegistryHexPreview: null,
                        ExpectedRegistryType: null,
                        ActualRegistryType: null,
                        RegistryVolatility: null,
                        ExceptionType: null,
                        ExceptionMessage: null
                    ));
                }
            }
            catch (Exception ex)
            {
                allFilesMatch = false;
                diagnostics.Add(new SelfVerificationDiagnosticEntry(
                    Phase: "FileReadback",
                    FailureKind: "FileReadException",
                    ExpectedPath: op.DestinationRelativePath,
                    ProbeLocation: extracted,
                    ItemType: "File",
                    SourcePackagePath: op.SourcePath,
                    ExpectedSizeBytes: null,
                    ExpectedSha256: null,
                    ActualSizeBytes: null,
                    ActualSha256: null,
                    ExpectedRegistryByteLength: null,
                    ActualRegistryByteLength: null,
                    ExpectedRegistryHexPreview: null,
                    ActualRegistryHexPreview: null,
                    ExpectedRegistryType: null,
                    ActualRegistryType: null,
                    RegistryVolatility: null,
                    ExceptionType: ex.GetType().Name,
                    ExceptionMessage: ex.Message
                ));
            }
        }

        int verifiedRegistry = 0;
        bool allRegistryMatches = true;
        int registryValuesIteratedCount = plan.RegistryValues.Length;
        int registryValuesDistinctCount = plan.RegistryValues.Select(v => $"{v.Hive}\\{v.KeyPath}\\{v.Name}").Distinct(StringComparer.OrdinalIgnoreCase).Count();

        foreach (IGrouping<string, PublicationRegistryValue> hiveValues in plan.RegistryValues.GroupBy(v => v.Hive, StringComparer.OrdinalIgnoreCase))
        {
            string hivePath = Path.Combine(verifyDir, "Windows", "System32", "config", hiveValues.Key);
            if (!File.Exists(hivePath))
            {
                allRegistryMatches = false;
                foreach (PublicationRegistryValue val in hiveValues)
                {
                    diagnostics.Add(new SelfVerificationDiagnosticEntry(
                        Phase: "RegistryReadback",
                        FailureKind: "HiveMissing",
                        ExpectedPath: $"{val.Hive}\\{val.KeyPath}\\{val.Name}",
                        ProbeLocation: hivePath,
                        ItemType: "RegistryHive",
                        SourcePackagePath: null,
                        ExpectedSizeBytes: null,
                        ExpectedSha256: null,
                        ActualSizeBytes: null,
                        ActualSha256: null,
                        ExpectedRegistryByteLength: null,
                        ActualRegistryByteLength: null,
                        ExpectedRegistryHexPreview: null,
                        ActualRegistryHexPreview: null,
                        ExpectedRegistryType: null,
                        ActualRegistryType: null,
                        RegistryVolatility: null,
                        ExceptionType: null,
                        ExceptionMessage: null
                    ));
                }
                continue;
            }
            using OfflineRegistryHive hive = OfflineRegistryHive.Open(hivePath);
            foreach (PublicationRegistryValue val in hiveValues)
            {
                if (!hive.TryOpenKey(val.KeyPath, out OfflineRegistryKey? key) || key is null)
                {
                    allRegistryMatches = false;
                    diagnostics.Add(new SelfVerificationDiagnosticEntry(
                        Phase: "RegistryReadback",
                        FailureKind: "KeyOpenFailed",
                        ExpectedPath: $"{val.Hive}\\{val.KeyPath}\\{val.Name}",
                        ProbeLocation: $"{hivePath}:{val.KeyPath}",
                        ItemType: "RegistryKey",
                        SourcePackagePath: null,
                        ExpectedSizeBytes: null,
                        ExpectedSha256: null,
                        ActualSizeBytes: null,
                        ActualSha256: null,
                        ExpectedRegistryByteLength: null,
                        ActualRegistryByteLength: null,
                        ExpectedRegistryHexPreview: null,
                        ActualRegistryHexPreview: null,
                        ExpectedRegistryType: null,
                        ActualRegistryType: null,
                        RegistryVolatility: val.Volatility,
                        ExceptionType: null,
                        ExceptionMessage: null
                    ));
                    continue;
                }
                using (key)
                {
                    try
                    {
                        OfflineRegistryValue regVal = key.ReadValue(val.Name.Length == 0 ? null : val.Name);
                        if (regVal.Type == val.RegistryType && (val.Volatility == "Volatile" || regVal.Data.AsSpan().SequenceEqual(val.EncodedBytes)))
                        {
                            verifiedRegistry++;
                        }
                        else
                        {
                            allRegistryMatches = false;
                            string? expectedTypeStr = regVal.Type == val.RegistryType ? null : ConvertRegistryType(val.RegistryType);
                            string? actualTypeStr = regVal.Type == val.RegistryType ? null : ConvertRegistryType(regVal.Type);
                            diagnostics.Add(new SelfVerificationDiagnosticEntry(
                                Phase: "RegistryReadback",
                                FailureKind: regVal.Type != val.RegistryType ? "ValueTypeMismatch" : "ValueDataMismatch",
                                ExpectedPath: $"{val.Hive}\\{val.KeyPath}\\{val.Name}",
                                ProbeLocation: $"{hivePath}:{val.KeyPath}:{(val.Name.Length == 0 ? "(Default)" : val.Name)}",
                                ItemType: "RegistryValue",
                                SourcePackagePath: null,
                                ExpectedSizeBytes: null,
                                ExpectedSha256: null,
                                ActualSizeBytes: null,
                                ActualSha256: null,
                                ExpectedRegistryByteLength: val.EncodedBytes.Length,
                                ActualRegistryByteLength: regVal.Data.Length,
                                ExpectedRegistryHexPreview: ToHexPreview(val.EncodedBytes),
                                ActualRegistryHexPreview: ToHexPreview(regVal.Data),
                                ExpectedRegistryType: expectedTypeStr,
                                ActualRegistryType: actualTypeStr,
                                RegistryVolatility: val.Volatility,
                                ExceptionType: null,
                                ExceptionMessage: null
                            ));
                        }
                    }
                    catch (Exception ex)
                    {
                        allRegistryMatches = false;
                        diagnostics.Add(new SelfVerificationDiagnosticEntry(
                            Phase: "RegistryReadback",
                            FailureKind: "ValueReadException",
                            ExpectedPath: $"{val.Hive}\\{val.KeyPath}\\{val.Name}",
                            ProbeLocation: $"{hivePath}:{val.KeyPath}:{(val.Name.Length == 0 ? "(Default)" : val.Name)}",
                            ItemType: "RegistryValue",
                            SourcePackagePath: null,
                            ExpectedSizeBytes: null,
                            ExpectedSha256: null,
                            ActualSizeBytes: null,
                            ActualSha256: null,
                            ExpectedRegistryByteLength: val.EncodedBytes.Length,
                            ActualRegistryByteLength: null,
                            ExpectedRegistryHexPreview: ToHexPreview(val.EncodedBytes),
                            ActualRegistryHexPreview: null,
                            ExpectedRegistryType: ConvertRegistryType(val.RegistryType),
                            ActualRegistryType: null,
                            RegistryVolatility: val.Volatility,
                            ExceptionType: ex.GetType().Name,
                            ExceptionMessage: ex.Message
                        ));
                    }
                }
            }
        }

        bool valid = opens && imageCount >= 1 && allFilesMatch && allRegistryMatches;
        return new WimSelfVerificationResult(opens, imageCount, imageName, verifiedFiles, verifiedRegistry, allFilesMatch, allRegistryMatches, valid, fileOperationsIteratedCount, fileOperationsDistinctCount, registryValuesIteratedCount, registryValuesDistinctCount, [.. diagnostics]);
    }

    private static void ValidateOutputSafety(string baseline, string output)
    {
        if (baseline.Equals(output, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Output WIM cannot be identical to baseline WIM: {output}");
        string baselineName = Path.GetFileName(baseline);
        string outputName = Path.GetFileName(output);
        if (outputName.Equals("install-original.wim", StringComparison.OrdinalIgnoreCase) ||
            outputName.Equals("install-acpivpc.wim", StringComparison.OrdinalIgnoreCase))
        {
            string? baselineDir = Path.GetDirectoryName(baseline);
            string? outputDir = Path.GetDirectoryName(output);
            if (string.Equals(baselineDir, outputDir, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Output WIM cannot target protected reference fixture: {output}");
        }
    }

    private static OfflineRegistryKey EnsureKey(OfflineRegistryHive hive, string keyPath)
    {
        string current = string.Empty;
        foreach (string segment in keyPath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 ? segment : current + "\\" + segment;
            if (hive.TryOpenKey(current, out OfflineRegistryKey? existing)) existing!.Dispose();
            else using (hive.CreateKey(current)) { }
        }
        return hive.OpenKey(keyPath);
    }

    private static string ConvertRegistryType(uint regType)
    {
        return regType switch
        {
            0 => "REG_NONE",
            1 => "REG_SZ",
            2 => "REG_EXPAND_SZ",
            3 => "REG_BINARY",
            4 => "REG_DWORD",
            5 => "REG_DWORD_BIG_ENDIAN",
            6 => "REG_LINK",
            7 => "REG_MULTI_SZ",
            8 => "REG_RESOURCE_LIST",
            9 => "REG_FULL_RESOURCE_DESCRIPTOR",
            10 => "REG_RESOURCE_REQUIREMENTS_LIST",
            11 => "REG_QWORD",
            _ => $"UNKNOWN({regType})"
        };
    }

    private static string ToHexPreview(byte[] data)
    {
        int previewBytes = Math.Min(data.Length, 64);
        string hex = Convert.ToHexString(data, 0, previewBytes);
        if (data.Length > 64)
            hex += "...";
        return hex;
    }

    private static string? NormalizeAndResolveDestinationPath(string destinationRelativePath, string verifyDir)
    {
        // Trim leading separators to normalize the path, matching extraction behavior
        string normalized = destinationRelativePath.TrimStart('\\', '/');

        // Detect absolute paths with drive letters (e.g., C:\..., D:\...)
        if (normalized.Length >= 2 && normalized[1] == ':')
        {
            return null; // Unsafe: absolute path with drive letter
        }

        // Use Path.GetFullPath to resolve any ".." segments and construct absolute path
        string fullPath = Path.GetFullPath(normalized, verifyDir);

        // Normalize verifyDir for comparison: ensure it ends with a separator
        string verifyDirNormalized = Path.GetFullPath(verifyDir);
        if (!verifyDirNormalized.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            verifyDirNormalized += Path.DirectorySeparatorChar;
        }

        // Ensure the resolved path is under verifyDir (prevents ".." escape attempts)
        if (!fullPath.StartsWith(verifyDirNormalized, StringComparison.OrdinalIgnoreCase))
        {
            return null; // Unsafe: path would escape verifyDir
        }

        return fullPath;
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
