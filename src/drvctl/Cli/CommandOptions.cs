namespace DrvCtl.Cli;

internal abstract record CommandOptions;

internal sealed record ExportCommandOptions(
    string Destination,
    int? Workers,
    bool Benchmark
) : CommandOptions;

internal sealed record VerifyCommandOptions(
    string Destination,
    int? Workers,
    bool Benchmark,
    bool FlushCache
) : CommandOptions;

internal sealed record ListCommandOptions(
    int? Workers
) : CommandOptions;

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

internal sealed class UsageException(string message) : Exception(message);
