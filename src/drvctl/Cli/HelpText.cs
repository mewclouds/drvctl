namespace DrvCtl.Cli;

internal static class HelpText
{
    internal const string Version = "1.0.0";

    internal static void PrintGeneral()
    {
        Console.WriteLine();
        Console.WriteLine($"drvctl {Version}");
        Console.WriteLine("Focused Windows driver package tooling");
        Console.WriteLine();
        Console.WriteLine("USAGE");
        Console.WriteLine(
            "  drvctl export <path> [--workers 1-4] [--benchmark]"
        );
        Console.WriteLine(
            "  drvctl verify <path> [--workers 1-4] [--benchmark] [--flush-cache]"
        );
        Console.WriteLine(
            "  drvctl list [--workers 1-4]"
        );
        Console.WriteLine("  drvctl inspect-inf <path-to-inf>");
        Console.WriteLine("  drvctl inspect-wim <path-to-wim>");
        Console.WriteLine("  drvctl plan-driver <package-directory>");
        Console.WriteLine("  drvctl validate-plan <package-directory>");
        Console.WriteLine("  drvctl simulate-apply <package-directory> --workspace <directory> --system-hive <path>");
        Console.WriteLine("  drvctl analyze-publication <baseline-wim> <serviced-wim> <package-directory> [--index <n>] [--workspace <directory>]");
        Console.WriteLine("  drvctl prototype-publication <baseline-wim> <reference-wim> <package-directory> --workspace <directory> [--index <n>]");
        Console.WriteLine("  drvctl prototype-inject-wim <baseline-wim> <output-wim> <package-directory> [--workspace <directory>] [--index <n>]");
        Console.WriteLine(
            "  drvctl help"
        );
        Console.WriteLine();
        Console.WriteLine("  inspect-inf / inspect-wim");
        Console.WriteLine("      Developer inspection commands using SetupAPI and wimlib directly.");
        Console.WriteLine();
        Console.WriteLine("  plan-driver");
        Console.WriteLine("      Produces a deterministic read-only driver staging plan.");
        Console.WriteLine();
        Console.WriteLine("  validate-plan");
        Console.WriteLine("      Compares a read-only plan with a known-good semantic fixture.");
        Console.WriteLine();
        Console.WriteLine("  simulate-apply");
        Console.WriteLine("      Applies only understood reflection operations to disposable copies.");
        Console.WriteLine();
        Console.WriteLine("  analyze-publication");
        Console.WriteLine("      Losslessly compares publication state in baseline and serviced WIMs.");
        Console.WriteLine();
        Console.WriteLine("  prototype-publication");
        Console.WriteLine("      Research-only publication into a newly extracted disposable directory tree.");
        Console.WriteLine();
        Console.WriteLine("  prototype-inject-wim");
        Console.WriteLine("      Research-only direct publication into a copied WIM image via libwim mutation.");
        Console.WriteLine();
        Console.WriteLine("COMMANDS");
        Console.WriteLine("  export");
        Console.WriteLine(
            "      Fast production export using SetupAPI and Windows CopyFile2."
        );
        Console.WriteLine(
            "      DISM is never called by this command."
        );
        Console.WriteLine();
        Console.WriteLine("  verify");
        Console.WriteLine(
            "      Exports with DISM first, then exports with drvctl, then compares"
        );
        Console.WriteLine(
            "      regular-file relative paths, byte lengths, and SHA-256 contents."
        );
        Console.WriteLine(
            "      The temporary DISM reference is deleted when verification ends."
        );
        Console.WriteLine();
        Console.WriteLine("  list");
        Console.WriteLine(
            "      Resolves published OEM INFs through SetupAPI and prints the results."
        );
        Console.WriteLine();
        Console.WriteLine("Run 'drvctl export --help', 'drvctl verify --help', or 'drvctl list --help' for details.");
        Console.WriteLine();
    }

    internal static void PrintExport()
    {
        Console.WriteLine();
        Console.WriteLine($"drvctl {Version} export");
        Console.WriteLine();
        Console.WriteLine("USAGE");
        Console.WriteLine(
            "  drvctl export <path> [--workers 1-4] [--benchmark]"
        );
        Console.WriteLine();
        Console.WriteLine("OPTIONS");
        Console.WriteLine("  --workers <1-4>");
        Console.WriteLine(
            "      Optional copy concurrency. Automatic when omitted."
        );
        Console.WriteLine();
        Console.WriteLine("  --benchmark");
        Console.WriteLine(
            "      Prints drvctl export timings."
        );
        Console.WriteLine();
        Console.WriteLine(
            "The destination must be new or empty. The production export path"
        );
        Console.WriteLine(
            "does not call DISM and does not perform hash verification."
        );
        Console.WriteLine();
    }

    internal static void PrintVerify()
    {
        Console.WriteLine();
        Console.WriteLine($"drvctl {Version} verify");
        Console.WriteLine();
        Console.WriteLine("USAGE");
        Console.WriteLine(
            "  drvctl verify <path> [--workers 1-4] [--benchmark] [--flush-cache]"
        );
        Console.WriteLine();
        Console.WriteLine("OPTIONS");
        Console.WriteLine("  --workers <1-4>");
        Console.WriteLine(
            "      Worker count used by drvctl export and parallel hashing."
        );
        Console.WriteLine();
        Console.WriteLine("  --benchmark");
        Console.WriteLine(
            "      Prints DISM time, drvctl timings, hash time, and speed comparison."
        );
        Console.WriteLine();
        Console.WriteLine("  --flush-cache");
        Console.WriteLine(
            "      Requests a Windows system file cache flush before DISM and again"
        );
        Console.WriteLine(
            "      before drvctl. This is cache-flushed, not guaranteed fresh-boot cold."
        );
        Console.WriteLine();
        Console.WriteLine(
            "verify always uses DISM as the reference exporter. There is no"
        );
        Console.WriteLine(
            "standalone verification mode in this build."
        );
        Console.WriteLine();
    }

    internal static void PrintList()
    {
        Console.WriteLine();
        Console.WriteLine($"drvctl {Version} list");
        Console.WriteLine();
        Console.WriteLine("USAGE");
        Console.WriteLine(
            "  drvctl list [--workers 1-4]"
        );
        Console.WriteLine();
        Console.WriteLine("OPTIONS");
        Console.WriteLine("  --workers <1-4>");
        Console.WriteLine(
            "      Optional SetupAPI resolution concurrency. Automatic when omitted."
        );
        Console.WriteLine();
        Console.WriteLine(
            "Lists published OEM INFs, their Driver Store packages, and resolved INF paths."
        );
        Console.WriteLine(
            "This command does not copy files, call DISM, hash files, or verify exports."
        );
        Console.WriteLine();
    }
}
