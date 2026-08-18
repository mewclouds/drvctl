/*
 * All user-facing help text. Deliberately only covers export, list, and
 * help, the public command surface. Hidden research commands have no entry
 * here, so they never appear in `drvctl help` even though CommandLine.Parse
 * still dispatches them by name.
 */

using DrvCtl.Core;

namespace DrvCtl.Cli;

internal static class HelpText
{
    internal static string Version => VersionInfo.Current;

    /// `drvctl help` / `drvctl --help` / `drvctl` with no arguments after a usage error.
    internal static void PrintGeneral()
    {
        Console.WriteLine();
        Console.WriteLine($"drvctl {Version}");
        Console.WriteLine("Focused Windows driver package tooling");
        Console.WriteLine();
        Console.WriteLine("USAGE");
        Console.WriteLine(
            "  drvctl export <path> [options]"
        );
        Console.WriteLine(
            "  drvctl list [options]"
        );
        Console.WriteLine(
            "  drvctl help"
        );
        Console.WriteLine();
        Console.WriteLine("COMMANDS");
        Console.WriteLine("  export");
        Console.WriteLine(
            "      Export installed third-party driver packages."
        );
        Console.WriteLine();
        Console.WriteLine("  list");
        Console.WriteLine(
            "      Show the third-party drivers published on this Windows installation."
        );
        Console.WriteLine();
        Console.WriteLine("Run 'drvctl export --help' or 'drvctl list --help' for details.");
        Console.WriteLine();
    }

    /// `drvctl export --help`.
    internal static void PrintExport()
    {
        Console.WriteLine();
        Console.WriteLine($"drvctl {Version} export");
        Console.WriteLine();
        Console.WriteLine("USAGE");
        Console.WriteLine(
            "  drvctl export <path>"
        );
        Console.WriteLine(
            "  drvctl export <path> --verify"
        );
        Console.WriteLine(
            "  drvctl export <path> --full-verify"
        );
        Console.WriteLine(
            "  drvctl export <path> --dism"
        );
        Console.WriteLine();
        Console.WriteLine(
            "Export installed third-party driver packages. Nothing extra by default."
        );
        Console.WriteLine();
        Console.WriteLine("OPTIONS");
        Console.WriteLine("  --verify");
        Console.WriteLine(
            "      Quick confidence. Checks file count, relative paths, and sizes"
        );
        Console.WriteLine(
            "      against the Driver Store source."
        );
        Console.WriteLine();
        Console.WriteLine("  --full-verify");
        Console.WriteLine(
            "      Expensive confidence. Adds SHA-256 so every byte has receipts."
        );
        Console.WriteLine();
        Console.WriteLine("  --dism");
        Console.WriteLine(
            "      Challenge Windows itself. Creates a temporary DISM reference"
        );
        Console.WriteLine(
            "      export and compares file count, paths, sizes, and SHA-256."
        );
        Console.WriteLine();
        Console.WriteLine("  --verbose");
        Console.WriteLine(
            "      Show the technical details."
        );
        Console.WriteLine();
        Console.WriteLine("  --benchmark");
        Console.WriteLine(
            "      Show timing and throughput data."
        );
        Console.WriteLine();
        Console.WriteLine(
            "--verify, --full-verify, and --dism are mutually exclusive."
        );
        Console.WriteLine(
            "The destination must be new or empty. A plain export never calls DISM."
        );
        Console.WriteLine();
    }

    /// `drvctl list --help`.
    internal static void PrintList()
    {
        Console.WriteLine();
        Console.WriteLine($"drvctl {Version} list");
        Console.WriteLine();
        Console.WriteLine("USAGE");
        Console.WriteLine(
            "  drvctl list [options]"
        );
        Console.WriteLine();
        Console.WriteLine(
            "Shows the third-party driver packages Windows has published."
        );
        Console.WriteLine();
        Console.WriteLine("OPTIONS");
        Console.WriteLine("  --verbose");
        Console.WriteLine(
            "      Show Driver Store paths and additional package details."
        );
        Console.WriteLine();
        Console.WriteLine("  --provider <text>");
        Console.WriteLine(
            "      Only show packages whose provider contains this text."
        );
        Console.WriteLine();
        Console.WriteLine("  --class <text>");
        Console.WriteLine(
            "      Only show packages whose class contains this text."
        );
        Console.WriteLine();
        Console.WriteLine(
            "This command does not copy files, call DISM, or verify exports."
        );
        Console.WriteLine();
    }
}
