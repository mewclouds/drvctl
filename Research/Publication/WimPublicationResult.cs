namespace DrvCtl.Publication;

internal sealed record WimPublicationResult(
    string BaselineWim,
    string OutputWim,
    string PackageDirectory,
    int ImageIndex,
    string Workspace,
    int AttemptedPackagesCount,
    int ProcessedPackagesCount,
    string BaselineSha256Before,
    string BaselineSha256After,
    string OutputSha256,
    long OutputSizeBytes,
    WimPublicationTimings Timings,
    WimSelfVerificationResult SelfVerification,
    string[] GeneratedFiles,
    string[] GeneratedRegistryValues,
    string[] OmittedOperations,
    bool WimMutationSuccess
);

internal sealed record WimPublicationTimings(
    long BaselineCopyMs,
    long PlanMs,
    long HiveExtractMs,
    long HiveMutationMs,
    long WimUpdatePreparationMs,
    long WimWriteMs,
    long SelfVerificationMs,
    long TotalCommandMs
);

internal sealed record WimSelfVerificationResult(
    bool WimOpens,
    int ImageCount,
    string? ImageName,
    int VerifiedFileCount,
    int VerifiedRegistryCount,
    bool AllFilesMatch,
    bool AllRegistryMatches,
    bool Valid,
    int FileOperationsIteratedCount,
    int FileOperationsDistinctCount,
    int RegistryValuesIteratedCount,
    int RegistryValuesDistinctCount,
    SelfVerificationDiagnosticEntry[] Diagnostics
);

internal sealed record SelfVerificationDiagnosticEntry(
    string Phase,
    string FailureKind,
    string? ExpectedPath,
    string? ProbeLocation,
    string? ItemType,
    string? SourcePackagePath,
    long? ExpectedSizeBytes,
    string? ExpectedSha256,
    long? ActualSizeBytes,
    string? ActualSha256,
    int? ExpectedRegistryByteLength,
    int? ActualRegistryByteLength,
    string? ExpectedRegistryHexPreview,
    string? ActualRegistryHexPreview,
    string? ExpectedRegistryType,
    string? ActualRegistryType,
    string? RegistryVolatility,
    string? ExceptionType,
    string? ExceptionMessage
);
