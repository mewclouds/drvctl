using DrvCtl.Export;

namespace DrvCtl.Benchmarking;

internal static class BenchmarkPrinter
{
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

    internal static void PrintComparison(
        double dismSeconds,
        ExportResult drvctlResult,
        bool cacheFlushed
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

        if (cacheFlushed)
        {
            Console.WriteLine(
                "Cache flushes were requested before both exporters."
            );
            Console.WriteLine(
                "This is cache-flushed, not guaranteed fresh-boot cold."
            );
        }
        else
        {
            Console.WriteLine(
                "DISM ran first and can warm filesystem cache for drvctl."
            );
            Console.WriteLine(
                "Use --flush-cache for the cache-flushed comparison."
            );
        }
    }
}
