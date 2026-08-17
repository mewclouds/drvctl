using DrvCtl.Cli;
using DrvCtl.Export;

namespace DrvCtl.Utilities;

internal static class ConsoleOutput
{
    internal static void PrintExportHeader(
        string destination,
        int publishedInfCount,
        int packageCount,
        int workers,
        string copyEngine
    )
    {
        Console.WriteLine();
        Console.WriteLine(
            "===================================================="
        );
        Console.WriteLine(
            $" drvctl {HelpText.Version}"
        );
        Console.WriteLine(
            "===================================================="
        );
        Console.WriteLine();
        Console.WriteLine(
            $"Destination        : {destination}"
        );
        Console.WriteLine(
            $"Published OEM INFs : {publishedInfCount}"
        );
        Console.WriteLine(
            $"Unique packages    : {packageCount}"
        );
        Console.WriteLine(
            $"Logical CPUs       : {Environment.ProcessorCount}"
        );
        Console.WriteLine(
            $"Workers            : {workers}"
        );
        Console.WriteLine(
            $"Copy engine        : {copyEngine}"
        );
    }

    internal static void PrintExportSummary(
        ExportResult result,
        bool workersWereManual
    )
    {
        Console.WriteLine();
        Console.WriteLine(
            "Export complete."
        );
        Console.WriteLine(
            $"  Packages : {result.PackageCount}"
        );
        Console.WriteLine(
            $"  Files    : {result.FileCount}"
        );
        Console.WriteLine(
            $"  Size     : {Formatters.Bytes(result.TotalBytes)}"
        );
        Console.WriteLine(
            $"  Output   : {result.Destination}"
        );
        Console.WriteLine(
            $"  Workers  : {result.Workers} ({(workersWereManual ? "manual" : "automatic")})"
        );
    }
}
