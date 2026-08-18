using DrvCtl.Cli;
using DrvCtl.Core;
using DrvCtl.Export;
using DrvCtl.Verification;

/*
 * Rendering only. This file owns the friendly-by-default, --verbose-for-detail
 * split that shapes every command's output. It never makes decisions about
 * what to check, only how to print what was already checked.
 */

namespace DrvCtl.Utilities;

internal static class ConsoleOutput
{
    /// --verbose preamble printed before the copy plan is built.
    internal static void PrintExportHeader(
        int publishedInfCount,
        int packageCount,
        WorkerSelection copyWorkers,
        string copyEngine
    )
    {
        Console.WriteLine();
        Console.WriteLine(
            "Technical detail"
        );
        Console.WriteLine(
            $"  Published OEM INFs : {publishedInfCount}"
        );
        Console.WriteLine(
            $"  Unique packages    : {packageCount}"
        );
        Console.WriteLine(
            $"  Logical CPUs       : {Environment.ProcessorCount}"
        );
        Console.WriteLine(
            $"  Copy workers       : {copyWorkers.Workers} ({copyWorkers.Label})"
        );
        Console.WriteLine(
            $"  Copy engine        : {copyEngine}"
        );
    }

    /// Friendly summary printed after every export. Adds a --verbose technical
    /// block underneath when requested. Skips its own "Done." when a
    /// validation mode is still to run, so the command's tail prints exactly
    /// one "Done." instead of a misleadingly early one followed by a real one.
    internal static void PrintExportSummary(
        ExportResult result,
        bool verbose,
        WorkerSelection copyWorkers,
        WorkerSelection? verificationWorkers,
        bool validationFollows = false
    )
    {
        Console.WriteLine();

        if (!validationFollows)
        {
            Console.WriteLine(
                "Done."
            );
        }

        Console.WriteLine(
            $"  Packages  {result.PackageCount}"
        );
        Console.WriteLine(
            $"  Files     {result.FileCount:N0}"
        );
        Console.WriteLine(
            $"  Size      {Formatters.Bytes(result.TotalBytes)}"
        );
        Console.WriteLine(
            $"  Time      {result.EndToEndSeconds:F2} s"
        );

        if (!verbose)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine(
            "Technical detail"
        );
        Console.WriteLine(
            $"  Output               : {result.Destination}"
        );
        Console.WriteLine(
            $"  Logical CPUs         : {result.LogicalCpuCount}"
        );
        Console.WriteLine(
            $"  Copy workers         : {copyWorkers.Workers} ({copyWorkers.Label})"
        );

        if (verificationWorkers.HasValue)
        {
            Console.WriteLine(
                $"  Verification workers : {verificationWorkers.Value.Workers} ({verificationWorkers.Value.Label})"
            );
        }

        Console.WriteLine(
            $"  Copy engine          : {result.CopyEngine}"
        );
    }

    /// Renders a --verify or --full-verify result against the Driver Store source.
    internal static void PrintSourceVerificationResult(
        TreeComparisonResult result,
        string modeLabel,
        string? successFlavor,
        bool verbose
    )
    {
        PrintValidationResult(
            result,
            "Verification",
            modeLabel,
            successFlavor,
            verbose
        );
    }

    /// Renders a --dism comparison result against the temporary DISM reference export.
    internal static void PrintDismComparisonResult(
        TreeComparisonResult result,
        bool verbose
    )
    {
        Console.WriteLine();

        if (result.ExactMatch)
        {
            Console.WriteLine(
                "DISM comparison passed."
            );
            Console.WriteLine(
                $"  Files  {result.RightFiles:N0}"
            );
            Console.WriteLine(
                "  Result Byte-for-byte match"
            );
        }
        else
        {
            Console.WriteLine(
                BuildFailureSummary(
                    result,
                    "DISM comparison"
                )
            );

            if (!verbose)
            {
                Console.WriteLine(
                    "Use --verbose for details."
                );
            }
        }

        if (verbose)
        {
            PrintTechnicalDetail(
                result
            );
        }
    }

    private static void PrintValidationResult(
        TreeComparisonResult result,
        string header,
        string modeLabel,
        string? successFlavor,
        bool verbose
    )
    {
        Console.WriteLine();

        if (result.ExactMatch)
        {
            Console.WriteLine(
                $"{header} passed."
            );
            Console.WriteLine(
                $"  Files  {result.RightFiles:N0}"
            );
            Console.WriteLine(
                $"  Mode   {modeLabel}"
            );

            if (successFlavor is not null)
            {
                Console.WriteLine(
                    successFlavor
                );
            }
        }
        else
        {
            Console.WriteLine(
                BuildFailureSummary(
                    result,
                    header
                )
            );

            if (!verbose)
            {
                Console.WriteLine(
                    "Use --verbose for details."
                );
            }
        }

        if (verbose)
        {
            PrintTechnicalDetail(
                result
            );
        }
    }

    private static void PrintTechnicalDetail(
        TreeComparisonResult result
    )
    {
        Console.WriteLine();
        Console.WriteLine(
            "Technical detail"
        );
        Console.WriteLine(
            $"  {result.LeftLabel} files      : {result.LeftFiles}"
        );
        Console.WriteLine(
            $"  {result.RightLabel} files      : {result.RightFiles}"
        );
        Console.WriteLine(
            $"  {result.LeftLabel} size        : {Formatters.Bytes(result.LeftBytes)}"
        );
        Console.WriteLine(
            $"  {result.RightLabel} size        : {Formatters.Bytes(result.RightBytes)}"
        );
        Console.WriteLine(
            $"  Missing from {result.RightLabel} : {result.MissingFromRight}"
        );
        Console.WriteLine(
            $"  Missing from {result.LeftLabel} : {result.MissingFromLeft}"
        );
        Console.WriteLine(
            $"  Size mismatches      : {result.SizeMismatches}"
        );

        if (result.HashesCompared)
        {
            Console.WriteLine(
                $"  SHA-256 mismatches   : {result.HashMismatches}"
            );
        }

        Console.WriteLine(
            $"  Verification time    : {result.Seconds:F3} s"
        );

        if (result.ExactMatch || result.Differences.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine(
            "Differences"
        );

        foreach (string difference in result.Differences)
        {
            Console.WriteLine(
                $"  {difference}"
            );
        }
    }

    private static string BuildFailureSummary(
        TreeComparisonResult result,
        string header
    )
    {
        List<string> parts = [];

        int missing =
            result.MissingFromLeft +
            result.MissingFromRight;

        if (missing > 0)
        {
            parts.Add(
                $"{missing} file{(missing == 1 ? string.Empty : "s")} " +
                (missing == 1 ? "is" : "are") +
                " missing"
            );
        }

        if (result.SizeMismatches > 0)
        {
            parts.Add(
                $"{result.SizeMismatches} file{(result.SizeMismatches == 1 ? string.Empty : "s")} " +
                $"{(result.SizeMismatches == 1 ? "has" : "have")} a different size"
            );
        }

        if (result.HashesCompared && result.HashMismatches > 0)
        {
            parts.Add(
                $"{result.HashMismatches} file{(result.HashMismatches == 1 ? string.Empty : "s")} " +
                $"{(result.HashMismatches == 1 ? "has" : "have")} a different hash"
            );
        }

        string detail =
            parts.Count switch
            {
                0 => "a mismatch was found",
                1 => parts[0],
                2 => $"{parts[0]} and {parts[1]}",
                _ => string.Join(", ", parts[..^1]) + ", and " + parts[^1]
            };

        return $"{header} failed: {detail}.";
    }
}
