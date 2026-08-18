/*
 * Parsed command shapes for every command CommandLine.Parse understands.
 * ExportCommandOptions, ListCommandOptions, and HelpCommandOptions back the
 * public CLI. Everything else here (InspectInf, InspectWim, PlanDriver,
 * ValidatePlan, SimulateApply, AnalyzePublication, PrototypePublication,
 * PrototypeInjectWim) backs a hidden research command: dispatchable by exact
 * name, but intentionally absent from HelpText and not part of the
 * supported public contract.
 */

namespace DrvCtl.Cli;

internal abstract record CommandOptions;

/// The three validation modes export --verify/--full-verify/--dism map to,
/// plus None for a plain export. Mutually exclusive at parse time.
internal enum ExportValidationMode
{
    None,
    Quick,
    Full,
    Dism
}

internal sealed record ExportCommandOptions(
    string Destination,
    bool Benchmark,
    bool Verbose,
    ExportValidationMode ValidationMode
) : CommandOptions;

internal sealed record ListCommandOptions(
    bool Verbose,
    string? ProviderFilter,
    string? ClassFilter
) : CommandOptions;

// Hidden research commands. See the file header above.
internal sealed record InspectInfCommandOptions(string Path) : CommandOptions;
internal sealed record InspectWimCommandOptions(string Path) : CommandOptions;
internal sealed record PlanDriverCommandOptions(string PackageDirectory) : CommandOptions;
internal sealed record ValidatePlanCommandOptions(string PackageDirectory) : CommandOptions;
internal sealed record SimulateApplyCommandOptions(
    string PackageDirectory,
    string Workspace,
    string SystemHive,
    string? SoftwareHive,
    string? DriversHive
) : CommandOptions;
internal sealed record AnalyzePublicationCommandOptions(
    string BaselineWim,
    string ServicedWim,
    string PackageDirectory,
    int ImageIndex,
    string Workspace
) : CommandOptions;
internal sealed record PrototypePublicationCommandOptions(
    string BaselineWim,
    string ReferenceWim,
    string PackageDirectory,
    int ImageIndex,
    string Workspace
) : CommandOptions;
internal sealed record PrototypeInjectWimCommandOptions(
    string BaselineWim,
    string OutputWim,
    string PackageDirectory,
    int ImageIndex,
    string Workspace,
    bool SkipSelfVerification = false
) : CommandOptions;

internal sealed record HelpCommandOptions : CommandOptions;

/// A malformed command line. Caught in DrvCtlApp.RunAsync and rendered as a
/// usage message plus general help, never a raw stack trace.
internal sealed class UsageException(string message) : Exception(message);
