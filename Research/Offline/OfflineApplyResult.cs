namespace DrvCtl.Offline;

internal sealed record OfflineApplyResult(
    OfflineApplyPlan Plan,
    OfflineFileCopyResult[] Files,
    OfflineRegistryWriteResult[] RegistryWrites,
    OfflineHiveResult[] Hives,
    OfflineVerificationResult[] Verification,
    string[] OutputFiles
)
{
    internal bool VerificationSucceeded => Verification.All(result => result.Succeeded);
}

internal sealed record OfflineFileCopyResult(
    string SourcePath,
    string OutputPath,
    long SourceSize,
    string SourceSha256,
    long OutputSize,
    string OutputSha256,
    bool Matches
);

internal sealed record OfflineRegistryWriteResult(
    string Hive,
    string KeyPath,
    string Name,
    string Type,
    string Value
);

internal sealed record OfflineHiveResult(
    string Name,
    string SourcePath,
    string InputCopyPath,
    string OutputPath,
    string SourceSha256Before,
    string SourceSha256After,
    bool SourceUnchanged,
    string OutputSha256,
    bool HasAppliedMutations
);

internal sealed record OfflineVerificationResult(
    string Name,
    bool Succeeded,
    string Detail
);
