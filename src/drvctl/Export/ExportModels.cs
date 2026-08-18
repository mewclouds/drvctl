/*
 * Plain data carried through the export pipeline. Kept separate from
 * DriverExporter so callers (CLI, benchmarking, console output) can depend on
 * the shapes without depending on the implementation.
 */

namespace DrvCtl.Export;

/// Input to <see cref="IDriverExporter.Export"/>.
internal sealed record ExportRequest(
    string Destination,
    int Workers,
    bool Verbose = false,
    bool WorkersFromOverride = false
);

/// Outcome and timing of a completed export, used for the console summary,
/// --benchmark output, and as input to source verification.
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
    double EndToEndSeconds,
    string[] PackageDirectories
)
{
    /// Sum of the three timed phases, excluding gaps such as staging-directory
    /// creation or the final atomic commit. Used only for --benchmark output.
    internal double CoreSeconds =>
        ResolveSeconds +
        BuildTreeSeconds +
        CopySeconds;

    /// Effective throughput over the full end-to-end wall time, not just the copy phase.
    internal double ThroughputMiBPerSecond =>
        TotalBytes /
        (1024.0 * 1024.0) /
        EndToEndSeconds;
}

/// One file to copy from a Driver Store package into the staging directory.
internal sealed record CopyJob(
    string Source,
    string Destination
);
