/*
 * --benchmark output only. Kept separate from ConsoleOutput because
 * performance detail is a distinct concern from friendly/verbose rendering,
 * gated by its own flag rather than folded into --verbose.
 */

using DrvCtl.Export;

namespace DrvCtl.Benchmarking;

internal static class BenchmarkPrinter
{
    /// Timing and throughput breakdown for a completed export.
    internal static void PrintExport(
        ExportResult result
    )
    {
        Console.WriteLine();
        Console.WriteLine(
            "drvctl benchmark"
        );
        Console.WriteLine(
            $"  SetupAPI resolution : {result.ResolveSeconds:F3} s"
        );
        Console.WriteLine(
            $"  Build tree          : {result.BuildTreeSeconds:F3} s"
        );
        Console.WriteLine(
            $"  Copy payload        : {result.CopySeconds:F3} s"
        );
        Console.WriteLine(
            $"  Core total          : {result.CoreSeconds:F3} s"
        );
        Console.WriteLine(
            $"  End-to-end total    : {result.EndToEndSeconds:F3} s"
        );
        Console.WriteLine(
            $"  Effective throughput: {result.ThroughputMiBPerSecond:F2} MiB/s"
        );
    }

    /// drvctl vs DISM timing comparison, shown only with `export --dism --benchmark`.
    /// Explicitly flags this as a warm-cache comparison since drvctl's own
    /// export always runs first (see RunExportAsync), then the DISM
    /// reference export runs second and can benefit from the filesystem
    /// cache drvctl's run just warmed.
    internal static void PrintComparison(
        double dismSeconds,
        ExportResult drvctlResult
    )
    {
        Console.WriteLine();
        Console.WriteLine(
            "External benchmark comparison"
        );
        Console.WriteLine(
            $"  DISM   : {dismSeconds:F3} s"
        );
        Console.WriteLine(
            $"  drvctl : {drvctlResult.EndToEndSeconds:F3} s"
        );

        if (
            dismSeconds > 0 &&
            drvctlResult.EndToEndSeconds > 0
        )
        {
            double speedup =
                dismSeconds /
                drvctlResult.EndToEndSeconds;

            double reduction =
                (
                    1.0 -
                    (
                        drvctlResult.EndToEndSeconds /
                        dismSeconds
                    )
                ) *
                100.0;

            Console.WriteLine(
                $"  Speedup             : {speedup:F2}x"
            );
            Console.WriteLine(
                $"  Wall-time reduction : {reduction:F2}%"
            );
        }

        Console.WriteLine();
        Console.WriteLine(
            "drvctl ran first and can warm filesystem cache for DISM."
        );
        Console.WriteLine(
            "This is not a cold-cache comparison."
        );
    }
}
