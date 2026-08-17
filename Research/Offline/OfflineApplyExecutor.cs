using System.Security.Cryptography;
using DrvCtl.Copy;
using DrvCtl.Registry;

namespace DrvCtl.Offline;

internal sealed class OfflineApplyExecutor(ICopyEngine copyEngine)
{
    internal OfflineApplyResult Execute(OfflineApplyPlan requestedPlan)
    {
        string workspace = OfflineWorkspaceSafety.Validate(requestedPlan);
        OfflineApplyPlan plan = requestedPlan with { Workspace = workspace };
        Dictionary<string, string> packageHashesBefore = plan.SourcePlan.StoreFiles.ToDictionary(file => file.SourcePath, file => Hash(file.SourcePath), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> hiveHashesBefore = plan.HiveInputs.ToDictionary(hive => hive.Name, hive => Hash(hive.SourcePath), StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(workspace);
        string inputHiveDirectory = Path.Combine(workspace, "InputHives");
        string outputHiveDirectory = Path.Combine(workspace, "OutputHives");
        Directory.CreateDirectory(inputHiveDirectory);
        Directory.CreateDirectory(outputHiveDirectory);

        List<OfflineHiveResult> hiveResults = [];
        foreach (OfflineHiveInput hive in plan.HiveInputs)
        {
            string inputCopy = Path.Combine(inputHiveDirectory, hive.Name);
            string output = Path.Combine(outputHiveDirectory, hive.Name);
            copyEngine.Copy(hive.SourcePath, inputCopy);
            ApplyHive(plan, hive.Name, inputCopy, output);
            string after = Hash(hive.SourcePath);
            bool hasMutations = plan.RegistryKeys.Any(key => key.Hive.Equals(hive.Name, StringComparison.OrdinalIgnoreCase)) || plan.RegistryValues.Any(value => value.Hive.Equals(hive.Name, StringComparison.OrdinalIgnoreCase));
            hiveResults.Add(new OfflineHiveResult(hive.Name, hive.SourcePath, inputCopy, output, hiveHashesBefore[hive.Name], after, hiveHashesBefore[hive.Name].Equals(after, StringComparison.Ordinal), Hash(output), hasMutations));
        }

        List<OfflineFileCopyResult> fileResults = [];
        foreach (OfflineFileCopy operation in plan.FileOperations)
        {
            string output = OfflineWorkspaceSafety.ResolveOutputPath(workspace, operation.DestinationRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            long sourceSize = new FileInfo(operation.SourcePath).Length;
            string sourceHash = Hash(operation.SourcePath);
            copyEngine.Copy(operation.SourcePath, output);
            long outputSize = new FileInfo(output).Length;
            string outputHash = Hash(output);
            fileResults.Add(new OfflineFileCopyResult(operation.SourcePath, output, sourceSize, sourceHash, outputSize, outputHash, sourceSize == outputSize && sourceHash.Equals(outputHash, StringComparison.Ordinal)));
        }

        List<OfflineVerificationResult> verification = Verify(plan, fileResults, hiveResults);
        foreach ((string source, string before) in packageHashesBefore)
        {
            string after = Hash(source);
            verification.Add(new OfflineVerificationResult($"Package source unchanged: {Path.GetFileName(source)}", before.Equals(after, StringComparison.Ordinal), $"Before={before}; After={after}"));
        }
        foreach (OfflineHiveResult hive in hiveResults)
        {
            verification.Add(new OfflineVerificationResult($"Source hive unchanged: {hive.Name}", hive.SourceUnchanged, $"Before={hive.SourceSha256Before}; After={hive.SourceSha256After}"));
            if (!hive.HasAppliedMutations)
                verification.Add(new OfflineVerificationResult($"No-op output hive unchanged: {hive.Name}", hive.OutputSha256.Equals(hive.SourceSha256Before, StringComparison.Ordinal), $"Input={hive.SourceSha256Before}; Output={hive.OutputSha256}"));
        }

        OfflineRegistryWriteResult[] writes = plan.RegistryValues.Select(value => new OfflineRegistryWriteResult(value.Hive, value.KeyPath, value.Name, value.Type == OfflineRegistryValueType.Dword ? "REG_DWORD" : "REG_SZ", value.Type == OfflineRegistryValueType.Dword ? value.DwordValue!.Value.ToString() : value.StringValue!)).ToArray();
        string[] outputs = Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return new OfflineApplyResult(plan, [.. fileResults], writes, [.. hiveResults], [.. verification], outputs);
    }

    private void ApplyHive(OfflineApplyPlan plan, string hiveName, string inputCopy, string output)
    {
        OfflineRegistryKeyCreate[] keys = plan.RegistryKeys.Where(key => key.Hive.Equals(hiveName, StringComparison.OrdinalIgnoreCase)).ToArray();
        OfflineRegistryValueSet[] values = plan.RegistryValues.Where(value => value.Hive.Equals(hiveName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (keys.Length == 0 && values.Length == 0)
        {
            copyEngine.Copy(inputCopy, output);
            return;
        }

        using OfflineRegistryHive hive = OfflineRegistryHive.Open(inputCopy);
        foreach (OfflineRegistryKeyCreate key in keys)
        {
            using OfflineRegistryKey created = hive.CreateKey(key.KeyPath);
        }
        foreach (IGrouping<string, OfflineRegistryValueSet> group in values.GroupBy(value => value.KeyPath, StringComparer.OrdinalIgnoreCase))
        {
            using OfflineRegistryKey key = hive.OpenKey(group.Key);
            foreach (OfflineRegistryValueSet value in group)
            {
                if (value.Type == OfflineRegistryValueType.Dword) key.SetDword(value.Name, value.DwordValue!.Value);
                else key.SetString(value.Name, value.StringValue!);
            }
        }
        hive.Save(output);
    }

    private static List<OfflineVerificationResult> Verify(OfflineApplyPlan plan, IReadOnlyList<OfflineFileCopyResult> files, IReadOnlyList<OfflineHiveResult> hives)
    {
        List<OfflineVerificationResult> results = [];
        foreach (OfflineFileCopyResult file in files)
            results.Add(new OfflineVerificationResult($"Reflected file: {Path.GetFileName(file.OutputPath)}", file.Matches, $"Source SHA256={file.SourceSha256}; Output SHA256={file.OutputSha256}"));

        foreach (IGrouping<string, OfflineRegistryValueSet> hiveValues in plan.RegistryValues.GroupBy(value => value.Hive, StringComparer.OrdinalIgnoreCase))
        {
            OfflineHiveResult hiveResult = hives.Single(hive => hive.Name.Equals(hiveValues.Key, StringComparison.OrdinalIgnoreCase));
            using OfflineRegistryHive hive = OfflineRegistryHive.Open(hiveResult.OutputPath);
            foreach (IGrouping<string, OfflineRegistryValueSet> keyValues in hiveValues.GroupBy(value => value.KeyPath, StringComparer.OrdinalIgnoreCase))
            {
                using OfflineRegistryKey key = hive.OpenKey(keyValues.Key);
                foreach (OfflineRegistryValueSet expected in keyValues)
                {
                    bool matches = expected.Type == OfflineRegistryValueType.Dword
                        ? key.ReadDword(expected.Name) == expected.DwordValue
                        : key.ReadString(expected.Name).Equals(expected.StringValue, StringComparison.Ordinal);
                    results.Add(new OfflineVerificationResult($"{expected.Hive}\\{expected.KeyPath}\\{expected.Name}", matches, matches ? "Saved value matches the apply plan." : "Saved value does not match the apply plan."));
                }
            }
        }
        return results;
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
