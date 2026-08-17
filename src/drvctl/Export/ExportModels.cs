namespace DrvCtl.Export;

internal sealed record ExportRequest(
    string Destination,
    int Workers
);

internal sealed record ExportResult(
    string Destination,
    string CopyEngine,
    int LogicalCpuCount,
    int Workers,
    int PublishedInfCount,
    int PackageCount,
    int FileCount,
    long TotalBytes,
    double ResolveSeconds,
    double BuildTreeSeconds,
    double CopySeconds,
    double EndToEndSeconds
)
{
    internal double CoreSeconds =>
        ResolveSeconds +
        BuildTreeSeconds +
        CopySeconds;

    internal double ThroughputMiBPerSecond =>
        TotalBytes /
        (1024.0 * 1024.0) /
        EndToEndSeconds;
}

internal sealed record CopyJob(
    string Source,
    string Destination
);
