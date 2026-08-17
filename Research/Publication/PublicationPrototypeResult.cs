namespace DrvCtl.Publication;

internal sealed record PublicationPrototypeResult(
    DriverPublicationPlan Plan,
    string BaselineWim,
    string ReferenceWim,
    int ImageIndex,
    string Workspace,
    string TreeRoot,
    string BaselineSha256Before,
    string BaselineSha256After,
    PublicationSourceHash[] SourcePackageHashes,
    PublicationAppliedFile[] AppliedFiles,
    PublicationAppliedRegistryValue[] AppliedRegistryValues,
    PublicationSemanticComparison Comparison,
    string VersionEvidence,
    string[] GeneratedFields,
    string[] OmittedFields,
    string[] UnsupportedFields,
    int ExactMatches,
    int SemanticMatches,
    int ExpectedDifferences,
    int UnsupportedOmissions,
    int Contradictions,
    string OfflineServicingAssessment,
    string FullPublicationAssessment,
    bool Complete
);

internal sealed record PublicationSourceHash(string Path, string BeforeSha256, string AfterSha256, bool Unchanged);
internal sealed record PublicationAppliedFile(string SourcePath, string DestinationPath, string SourceSha256, string DestinationSha256, long Size, bool Matches);
internal sealed record PublicationAppliedRegistryValue(string Hive, string KeyPath, string Name, string TypeName, string RawHex, string Derivation, EvidenceStatus EvidenceStatus);

internal enum PublicationComparisonStatus
{
    ExactMatch,
    SemanticallyEquivalent,
    ExpectedPrototypeDifference,
    Unsupported,
    Contradiction
}

internal sealed record PublicationComparisonItem(
    PublicationComparisonStatus Status,
    string Category,
    string Identity,
    string Detail
);

internal sealed record PublicationSemanticComparison(
    int ExactFileCount,
    PublicationComparisonItem[] Items,
    int ExactMatches,
    int SemanticallyEquivalent,
    int ExpectedDifferences,
    int Unsupported,
    int Contradictions
);
