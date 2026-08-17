using System.Security.Principal;
using DrvCtl.Benchmarking;
using DrvCtl.Cli;
using DrvCtl.Core;
using DrvCtl.Dism;
using DrvCtl.Export;
using DrvCtl.Platform;
using DrvCtl.Utilities;

namespace DrvCtl.Verification;

internal sealed class DismVerificationRunner(
    DismRunner dism,
    IDriverExporter exporter,
    FileTreeVerifier verifier,
    CacheFlusher cacheFlusher
)
{
    internal async Task<int> RunAsync(
        VerifyCommandOptions options,
        int workers
    )
    {
        if (!IsAdministrator())
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "drvctl verify requires an elevated terminal because DISM and"
            );
            Console.Error.WriteLine(
                "system file cache flushing require administrative privileges."
            );

            return ExitCodes.RuntimeFailure;
        }

        DestinationPreflight preflight;

        try
        {
            preflight =
                PathSafety.ValidateExportDestination(
                    options.Destination
                );
        }
        catch (Exception error)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "Verification preflight failed:"
            );
            Console.Error.WriteLine(
                $"  {error.Message}"
            );

            return ExitCodes.RuntimeFailure;
        }

        string dismReference =
            CreateDismReferencePath(
                preflight.Parent
            );

        try
        {
            Directory.CreateDirectory(
                dismReference
            );

            PrintHeader(
                preflight.Destination,
                workers,
                options
            );

            if (options.FlushCache)
            {
                Console.WriteLine(
                    "Flushing system file cache before DISM..."
                );

                cacheFlusher.Flush();

                Console.WriteLine(
                    "Cache flush request completed."
                );
                Console.WriteLine();
            }

            Console.WriteLine(
                "Running DISM reference export first..."
            );

            DismRunResult dismResult;

            try
            {
                dismResult =
                    await dism.ExportDriversAsync(
                        dismReference
                    );
            }
            catch (DismException error)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    error.Message
                );

                PrintDismFailureDetails(
                    error
                );

                return ExitCodes.DismFailure;
            }

            Console.WriteLine();

            if (options.FlushCache)
            {
                Console.WriteLine(
                    "Flushing system file cache before drvctl..."
                );

                cacheFlusher.Flush();

                Console.WriteLine(
                    "Cache flush request completed."
                );
                Console.WriteLine();
            }

            Console.WriteLine(
                "Running production drvctl exporter..."
            );

            ExportResult exportResult;

            try
            {
                exportResult =
                    exporter.Export(
                        new ExportRequest(
                            preflight.Destination,
                            workers
                        )
                    );
            }
            catch (Exception error)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    "drvctl export failed during verification:"
                );
                Console.Error.WriteLine(
                    $"  {error.Message}"
                );

                return ExitCodes.ExportFailureDuringVerification;
            }

            ConsoleOutput.PrintExportSummary(
                exportResult,
                options.Workers.HasValue
            );

            if (options.Benchmark)
            {
                BenchmarkPrinter.PrintExport(
                    exportResult
                );
            }

            Console.WriteLine();
            Console.WriteLine(
                "Running forensic hash diff..."
            );
            Console.WriteLine(
                "  relative path + size + SHA-256"
            );

            TreeComparisonResult comparison =
                verifier.Compare(
                    exportResult.Destination,
                    dismReference,
                    workers
                );

            PrintComparisonResult(
                comparison
            );

            if (!comparison.ExactMatch)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "FORENSIC DIFFERENCES"
                );

                foreach (
                    string difference in comparison.Differences
                )
                {
                    Console.WriteLine(
                        $"  {difference}"
                    );
                }

                Console.WriteLine();
                Console.WriteLine(
                    "FAIL: drvctl and DISM differ."
                );

                return ExitCodes.VerificationMismatch;
            }

            Console.WriteLine();
            Console.WriteLine(
                "PASS: drvctl and DISM contain the same regular-file"
            );
            Console.WriteLine(
                "      relative paths, sizes, and SHA-256 contents."
            );

            if (options.Benchmark)
            {
                BenchmarkPrinter.PrintComparison(
                    dismResult.Seconds,
                    exportResult,
                    options.FlushCache
                );
            }

            Console.WriteLine();
            Console.WriteLine(
                "Done."
            );

            return ExitCodes.Success;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "drvctl verify failed:"
            );
            Console.Error.WriteLine(
                $"  {error.Message}"
            );

            return ExitCodes.RuntimeFailure;
        }
        finally
        {
            if (Directory.Exists(dismReference))
            {
                Console.WriteLine();
                Console.WriteLine(
                    "Cleaning temporary DISM reference..."
                );

                try
                {
                    Directory.Delete(
                        dismReference,
                        recursive: true
                    );
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine(
                        $"Warning: could not remove temporary DISM reference '{dismReference}': {error.Message}"
                    );
                }
            }
        }
    }

    private static string CreateDismReferencePath(
        string parent
    )
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            string nonce =
                Guid.NewGuid()
                    .ToString("N")[..8];

            string candidate =
                Path.Combine(
                    parent,
                    ".dism-" + nonce
                );

            if (
                !Directory.Exists(candidate) &&
                !File.Exists(candidate)
            )
            {
                return candidate;
            }
        }

        throw new IOException(
            "Could not allocate a unique temporary DISM directory."
        );
    }

    private static bool IsAdministrator()
    {
        using WindowsIdentity identity =
            WindowsIdentity.GetCurrent();

        WindowsPrincipal principal =
            new(
                identity
            );

        return principal.IsInRole(
            WindowsBuiltInRole.Administrator
        );
    }

    private static void PrintHeader(
        string destination,
        int workers,
        VerifyCommandOptions options
    )
    {
        Console.WriteLine();
        Console.WriteLine(
            "===================================================="
        );
        Console.WriteLine(
            $" drvctl {HelpText.Version} verify"
        );
        Console.WriteLine(
            "===================================================="
        );
        Console.WriteLine();
        Console.WriteLine(
            $"drvctl output : {destination}"
        );
        Console.WriteLine(
            "DISM reference: temporary"
        );
        Console.WriteLine(
            $"Workers       : {workers} ({(options.Workers.HasValue ? "manual" : "automatic")})"
        );
        Console.WriteLine(
            $"Benchmark     : {options.Benchmark}"
        );
        Console.WriteLine(
            $"Flush cache   : {options.FlushCache}"
        );
        Console.WriteLine();
    }

    private static void PrintComparisonResult(
        TreeComparisonResult result
    )
    {
        Console.WriteLine();
        Console.WriteLine(
            "Verification result"
        );
        Console.WriteLine(
            $"  drvctl files        : {result.DrvCtlFiles}"
        );
        Console.WriteLine(
            $"  DISM files          : {result.DismFiles}"
        );
        Console.WriteLine(
            $"  drvctl size         : {Formatters.Bytes(result.DrvCtlBytes)}"
        );
        Console.WriteLine(
            $"  DISM size           : {Formatters.Bytes(result.DismBytes)}"
        );
        Console.WriteLine(
            $"  Missing from drvctl : {result.MissingFromDrvCtl}"
        );
        Console.WriteLine(
            $"  Missing from DISM   : {result.MissingFromDism}"
        );
        Console.WriteLine(
            $"  Size mismatches     : {result.SizeMismatches}"
        );
        Console.WriteLine(
            $"  SHA-256 mismatches  : {result.HashMismatches}"
        );
        Console.WriteLine(
            $"  Hash diff time      : {result.Seconds:F3} s"
        );
    }

    private static void PrintDismFailureDetails(
        DismException error
    )
    {
        string output =
            string.Join(
                Environment.NewLine,
                new[]
                {
                    error.StandardError.Trim(),
                    error.StandardOutput.Trim()
                }
                .Where(
                    text => !string.IsNullOrWhiteSpace(text)
                )
            );

        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(
            output
        );
    }
}
