using System.Diagnostics;

namespace DrvCtl.Dism;

internal sealed class DismRunner
{
    internal async Task<DismRunResult> ExportDriversAsync(
        string destination
    )
    {
        string windowsDirectory =
            Environment.GetFolderPath(
                Environment.SpecialFolder.Windows
            );

        string dismPath =
            Path.Combine(
                windowsDirectory,
                "System32",
                "dism.exe"
            );

        if (!File.Exists(dismPath))
        {
            throw new DismException(
                -1,
                $"DISM was not found at '{dismPath}'.",
                string.Empty,
                string.Empty
            );
        }

        ProcessStartInfo startInfo =
            new()
            {
                FileName = dismPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

        startInfo.ArgumentList.Add(
            "/Online"
        );
        startInfo.ArgumentList.Add(
            "/Export-Driver"
        );
        startInfo.ArgumentList.Add(
            $"/Destination:{destination}"
        );

        Stopwatch watch =
            Stopwatch.StartNew();

        using Process process =
            Process.Start(startInfo)
            ?? throw new DismException(
                -1,
                "Failed to start DISM.",
                string.Empty,
                string.Empty
            );

        Task<string> stdoutTask =
            process.StandardOutput.ReadToEndAsync();

        Task<string> stderrTask =
            process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        string stdout =
            await stdoutTask;

        string stderr =
            await stderrTask;

        watch.Stop();

        if (process.ExitCode != 0)
        {
            throw new DismException(
                process.ExitCode,
                $"DISM failed with exit code {process.ExitCode}.",
                stdout,
                stderr
            );
        }

        return new DismRunResult(
            process.ExitCode,
            watch.Elapsed.TotalSeconds
        );
    }
}

internal sealed record DismRunResult(
    int ExitCode,
    double Seconds
);

internal sealed class DismException(
    int exitCode,
    string message,
    string standardOutput,
    string standardError
) : Exception(message)
{
    internal int ExitCode { get; } =
        exitCode;

    internal string StandardOutput { get; } =
        standardOutput;

    internal string StandardError { get; } =
        standardError;
}
