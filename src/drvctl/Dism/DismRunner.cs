/*
 * Shells out to the real dism.exe. This is the only place drvctl calls DISM,
 * and only `--dism` reaches it - a plain export never touches this file.
 */

using System.Diagnostics;

namespace DrvCtl.Dism;

/// Runs `dism.exe /Online /Export-Driver` as a child process.
internal sealed class DismRunner
{
    /// Runs a DISM driver export to <paramref name="destination"/>, which must already exist.
    /// Requires an elevated process. DISM itself enforces that, not this method.
    /// <exception cref="DismException">dism.exe was not found, could not start, or exited non-zero.</exception>
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

/// Outcome of a successful (exit code 0) DISM run.
internal sealed record DismRunResult(
    int ExitCode,
    double Seconds
);

/// Thrown when DISM cannot be found, started, or exits non-zero. Carries the
/// captured stdout/stderr so --verbose can print DISM's own diagnostics.
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
