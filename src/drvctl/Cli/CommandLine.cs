namespace DrvCtl.Cli;

internal static class CommandLine
{
    private const int MinWorkers = 1;
    private const int MaxWorkers = 4;

    internal static CommandOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new UsageException(
                "No command was provided."
            );
        }

        string command =
            args[0].ToLowerInvariant();

        return command switch
        {
            "export" =>
                ParseExport(args[1..]),

            "verify" =>
                ParseVerify(args[1..]),

            "list" =>
                ParseList(args[1..]),

            "inspect-inf" => ParseSinglePath(args[1..], "inspect-inf", path => new InspectInfCommandOptions(path)),
            "inspect-wim" => ParseSinglePath(args[1..], "inspect-wim", path => new InspectWimCommandOptions(path)),
            "plan-driver" => ParseSinglePath(args[1..], "plan-driver", path => new PlanDriverCommandOptions(path)),
            "validate-plan" => ParseSinglePath(args[1..], "validate-plan", path => new ValidatePlanCommandOptions(path)),
            "simulate-apply" => ParseSimulateApply(args[1..]),
            "analyze-publication" => ParseAnalyzePublication(args[1..]),
            "prototype-publication" => ParsePrototypePublication(args[1..]),
            "prototype-inject-wim" => ParsePrototypeInjectWim(args[1..]),

            "help" or "--help" or "-h" or "-help" =>
                new HelpCommandOptions(),

            _ =>
                throw new UsageException(
                    $"Unknown command: {args[0]}"
                )
        };
    }

    private static PrototypePublicationCommandOptions ParsePrototypePublication(string[] args)
    {
        List<string> positional = [];
        int imageIndex = 1;
        string? workspace = null;
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (!argument.StartsWith('-')) { positional.Add(argument); continue; }
            if (index + 1 >= args.Length) throw new UsageException($"{argument} requires a value.");
            string value = args[++index];
            if (argument.Equals("--index", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out imageIndex) || imageIndex < 1) throw new UsageException("--index must be a positive image index.");
            }
            else if (argument.Equals("--workspace", StringComparison.OrdinalIgnoreCase)) workspace = SetOnce(workspace, value, argument);
            else throw new UsageException($"Unknown prototype-publication option: {argument}");
        }
        if (positional.Count != 3 || string.IsNullOrWhiteSpace(workspace))
            throw new UsageException("prototype-publication requires baseline WIM, reference WIM, package directory, and --workspace.");
        return new PrototypePublicationCommandOptions(positional[0], positional[1], positional[2], imageIndex, workspace);
    }

    private static PrototypeInjectWimCommandOptions ParsePrototypeInjectWim(string[] args)
    {
        List<string> positional = [];
        int imageIndex = 1;
        string? workspace = null;
        bool skipSelfVerification = false;
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (!argument.StartsWith('-')) { positional.Add(argument); continue; }
            if (argument.Equals("--skip-self-verification", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--benchmark", StringComparison.OrdinalIgnoreCase))
            {
                skipSelfVerification = true;
                continue;
            }
            if (index + 1 >= args.Length) throw new UsageException($"{argument} requires a value.");
            string value = args[++index];
            if (argument.Equals("--index", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out imageIndex) || imageIndex < 1) throw new UsageException("--index must be a positive image index.");
            }
            else if (argument.Equals("--workspace", StringComparison.OrdinalIgnoreCase)) workspace = SetOnce(workspace, value, argument);
            else throw new UsageException($"Unknown prototype-inject-wim option: {argument}");
        }
        if (positional.Count != 3)
            throw new UsageException("prototype-inject-wim requires baseline WIM, output WIM, and package directory.");
        workspace ??= Path.Combine(Environment.CurrentDirectory, $".publication-prototype\\wim-inject-{Path.GetFileNameWithoutExtension(positional[1])}");
        return new PrototypeInjectWimCommandOptions(positional[0], positional[1], positional[2], imageIndex, workspace, skipSelfVerification);
    }

    private static AnalyzePublicationCommandOptions ParseAnalyzePublication(string[] args)
    {
        List<string> positional = [];
        int imageIndex = 1;
        string? workspace = null;
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (!argument.StartsWith('-')) { positional.Add(argument); continue; }
            if (index + 1 >= args.Length) throw new UsageException($"{argument} requires a value.");
            string value = args[++index];
            if (argument.Equals("--index", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out imageIndex) || imageIndex < 1) throw new UsageException("--index must be a positive image index.");
            }
            else if (argument.Equals("--workspace", StringComparison.OrdinalIgnoreCase)) workspace = SetOnce(workspace, value, argument);
            else throw new UsageException($"Unknown analyze-publication option: {argument}");
        }
        if (positional.Count != 3) throw new UsageException("analyze-publication requires baseline WIM, serviced WIM, and package directory paths.");
        workspace ??= Path.Combine(Environment.CurrentDirectory, $"publication-analysis-{Path.GetFileNameWithoutExtension(positional[1])}-{Path.GetFileName(Path.TrimEndingDirectorySeparator(positional[2]))}-index{imageIndex}");
        return new AnalyzePublicationCommandOptions(positional[0], positional[1], positional[2], imageIndex, workspace);
    }

    private static SimulateApplyCommandOptions ParseSimulateApply(string[] args)
    {
        string? package = null;
        string? workspace = null;
        string? systemHive = null;
        string? softwareHive = null;
        string? driversHive = null;
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (!argument.StartsWith('-'))
            {
                if (package is not null) throw new UsageException($"Unexpected simulate-apply argument: {argument}");
                package = argument;
                continue;
            }
            if (index + 1 >= args.Length) throw new UsageException($"{argument} requires a path.");
            string value = args[++index];
            if (argument.Equals("--workspace", StringComparison.OrdinalIgnoreCase)) workspace = SetOnce(workspace, value, argument);
            else if (argument.Equals("--system-hive", StringComparison.OrdinalIgnoreCase)) systemHive = SetOnce(systemHive, value, argument);
            else if (argument.Equals("--software-hive", StringComparison.OrdinalIgnoreCase)) softwareHive = SetOnce(softwareHive, value, argument);
            else if (argument.Equals("--drivers-hive", StringComparison.OrdinalIgnoreCase)) driversHive = SetOnce(driversHive, value, argument);
            else throw new UsageException($"Unknown simulate-apply option: {argument}");
        }
        if (string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(systemHive))
            throw new UsageException("simulate-apply requires a package directory, --workspace, and --system-hive.");
        return new SimulateApplyCommandOptions(package, workspace, systemHive, softwareHive, driversHive);
    }

    private static string SetOnce(string? existing, string value, string option)
    {
        if (existing is not null) throw new UsageException($"{option} was provided more than once.");
        if (string.IsNullOrWhiteSpace(value)) throw new UsageException($"{option} requires a non-empty path.");
        return value;
    }

    private static CommandOptions ParseSinglePath(string[] args, string command, Func<string, CommandOptions> create)
    {
        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0])) throw new UsageException($"{command} requires exactly one path.");
        return create(args[0]);
    }

    private static ExportCommandOptions ParseExport(
        string[] args
    )
    {
        string? destination = null;
        int? workers = null;
        bool benchmark = false;

        for (int index = 0; index < args.Length;)
        {
            string argument = args[index];

            if (IsHelp(argument))
            {
                return HelpForExport();
            }

            if (IsOption(argument, "--benchmark", "-benchmark"))
            {
                benchmark = true;
                index++;
                continue;
            }

            if (IsOption(argument, "--workers", "-workers"))
            {
                workers =
                    ParseWorkers(
                        args,
                        ref index
                    );

                continue;
            }

            if (argument.StartsWith('-'))
            {
                throw new UsageException(
                    $"Unknown export option: {argument}"
                );
            }

            if (destination is not null)
            {
                throw new UsageException(
                    $"Unexpected export argument: {argument}"
                );
            }

            destination = argument;
            index++;
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new UsageException(
                "export requires a destination path."
            );
        }

        return new ExportCommandOptions(
            destination,
            workers,
            benchmark
        );
    }

    private static VerifyCommandOptions ParseVerify(
        string[] args
    )
    {
        string? destination = null;
        int? workers = null;
        bool benchmark = false;
        bool flushCache = false;

        for (int index = 0; index < args.Length;)
        {
            string argument = args[index];

            if (IsHelp(argument))
            {
                return HelpForVerify();
            }

            if (IsOption(argument, "--benchmark", "-benchmark"))
            {
                benchmark = true;
                index++;
                continue;
            }

            if (IsOption(argument, "--flush-cache", "-flushcache"))
            {
                flushCache = true;
                index++;
                continue;
            }

            if (IsOption(argument, "--workers", "-workers"))
            {
                workers =
                    ParseWorkers(
                        args,
                        ref index
                    );

                continue;
            }

            if (argument.StartsWith('-'))
            {
                throw new UsageException(
                    $"Unknown verify option: {argument}"
                );
            }

            if (destination is not null)
            {
                throw new UsageException(
                    $"Unexpected verify argument: {argument}"
                );
            }

            destination = argument;
            index++;
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new UsageException(
                "verify requires a destination path."
            );
        }

        return new VerifyCommandOptions(
            destination,
            workers,
            benchmark,
            flushCache
        );
    }

    private static ListCommandOptions ParseList(
        string[] args
    )
    {
        int? workers = null;

        for (int index = 0; index < args.Length;)
        {
            string argument = args[index];

            if (IsHelp(argument))
            {
                return HelpForList();
            }

            if (IsOption(argument, "--workers", "-workers"))
            {
                workers =
                    ParseWorkers(
                        args,
                        ref index
                    );

                continue;
            }

            if (argument.StartsWith('-'))
            {
                throw new UsageException(
                    $"Unknown list option: {argument}"
                );
            }

            throw new UsageException(
                $"Unexpected list argument: {argument}"
            );
        }

        return new ListCommandOptions(workers);
    }

    private static int ParseWorkers(
        string[] args,
        ref int index
    )
    {
        index++;

        if (index >= args.Length)
        {
            throw new UsageException(
                "--workers requires a number from 1 through 4."
            );
        }

        if (!int.TryParse(
            args[index],
            out int workers
        ))
        {
            throw new UsageException(
                "--workers must be a number from 1 through 4."
            );
        }

        if (
            workers < MinWorkers ||
            workers > MaxWorkers
        )
        {
            throw new UsageException(
                "--workers must be between 1 and 4."
            );
        }

        index++;
        return workers;
    }

    private static bool IsOption(
        string value,
        string longName,
        string powershellName
    )
    {
        return
            value.Equals(
                longName,
                StringComparison.OrdinalIgnoreCase
            ) ||
            value.Equals(
                powershellName,
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool IsHelp(string value)
    {
        return
            value.Equals(
                "--help",
                StringComparison.OrdinalIgnoreCase
            ) ||
            value.Equals(
                "-help",
                StringComparison.OrdinalIgnoreCase
            ) ||
            value.Equals(
                "-h",
                StringComparison.OrdinalIgnoreCase
            );
    }

    // These sentinels let the app print command-specific help without adding a
    // second parsing model just for help.
    private static ExportCommandOptions HelpForExport()
    {
        throw new CommandHelpException("export");
    }

    private static VerifyCommandOptions HelpForVerify()
    {
        throw new CommandHelpException("verify");
    }

    private static ListCommandOptions HelpForList()
    {
        throw new CommandHelpException("list");
    }
}

internal sealed class CommandHelpException(
    string command
) : Exception
{
    internal string Command { get; } = command;
}
