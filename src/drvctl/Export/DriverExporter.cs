/*
 * The plain export pipeline: resolve published packages, stage a copy plan,
 * copy in parallel, then atomically commit. This is the only place that
 * touches the copy path. Verification and DISM comparison are separate
 * concerns layered on afterward by the CLI, not by this class.
 */

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using DrvCtl.Copy;
using DrvCtl.Core;
using DrvCtl.Drivers;
using DrvCtl.Utilities;

namespace DrvCtl.Export;

/// Default <see cref="IDriverExporter"/> implementation.
internal sealed class DriverExporter(
    DriverStoreResolver resolver,
    ICopyEngine copyEngine
) : IDriverExporter
{
    private const int MaxReportedCopyFailures = 8;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The destination is unsafe, or package resolution failed.</exception>
    /// <exception cref="IOException">One or more files could not be copied. The incomplete export is removed.</exception>
    public ExportResult Export(
        ExportRequest request
    )
    {
        DestinationPreflight destination =
            PathSafety.ValidateExportDestination(
                request.Destination
            );

        Stopwatch endToEnd =
            Stopwatch.StartNew();

        Stopwatch resolveWatch =
            Stopwatch.StartNew();

        DriverStoreResolution resolution =
            resolver.Resolve(
                request.Workers
            );

        resolveWatch.Stop();

        Console.WriteLine();
        Console.WriteLine(
            $"Exporting {resolution.PackageDirectories.Length} driver package" +
            (resolution.PackageDirectories.Length == 1 ? string.Empty : "s") +
            $" to {destination.Destination}"
        );

        if (request.Verbose)
        {
            ConsoleOutput.PrintExportHeader(
                resolution.PublishedInfCount,
                resolution.PackageDirectories.Length,
                new WorkerSelection(request.Workers, request.WorkersFromOverride),
                copyEngine.Name
            );
        }

        using StagingDirectory staging =
            StagingDirectory.Create(
                destination.Parent
            );

        if (request.Verbose)
        {
            Console.WriteLine();
            Console.WriteLine(
                "Building copy plan..."
            );
        }

        Stopwatch treeWatch =
            Stopwatch.StartNew();

        List<CopyJob> jobs = [];
        long totalBytes = 0;

        BuildCopyPlan(
            resolution.PackageDirectories,
            staging.Path,
            jobs,
            ref totalBytes
        );

        treeWatch.Stop();

        if (request.Verbose)
        {
            Console.WriteLine(
                "Copying files..."
            );
        }

        Stopwatch copyWatch =
            Stopwatch.StartNew();

        CopyJobs(
            jobs,
            request.Workers
        );

        copyWatch.Stop();

        staging.Commit(
            destination.Destination,
            destination.ExistedEmpty
        );

        endToEnd.Stop();

        return new ExportResult(
            destination.Destination,
            copyEngine.Name,
            Environment.ProcessorCount,
            request.Workers,
            resolution.PublishedInfCount,
            resolution.PackageDirectories.Length,
            jobs.Count,
            totalBytes,
            resolveWatch.Elapsed.TotalSeconds,
            treeWatch.Elapsed.TotalSeconds,
            copyWatch.Elapsed.TotalSeconds,
            endToEnd.Elapsed.TotalSeconds,
            resolution.PackageDirectories
        );
    }

    /// Mirrors each package directory's structure under the staging root and
    /// appends one CopyJob per file. Directories are created up front so the
    /// parallel copy phase never races on directory creation.
    private static void BuildCopyPlan(
        string[] packageDirectories,
        string stagingRoot,
        List<CopyJob> jobs,
        ref long totalBytes
    )
    {
        foreach (
            string packageDirectory in packageDirectories
        )
        {
            DirectoryInfo packageInfo =
                new(
                    packageDirectory
                );

            if (!packageInfo.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Driver package directory disappeared: " +
                    packageDirectory
                );
            }

            string packageDestination =
                Path.Combine(
                    stagingRoot,
                    packageInfo.Name
                );

            Directory.CreateDirectory(
                packageDestination
            );

            foreach (
                string directory in Directory.EnumerateDirectories(
                    packageDirectory,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                string relative =
                    Path.GetRelativePath(
                        packageDirectory,
                        directory
                    );

                Directory.CreateDirectory(
                    Path.Combine(
                        packageDestination,
                        relative
                    )
                );
            }

            foreach (
                string source in Directory.EnumerateFiles(
                    packageDirectory,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                string relative =
                    Path.GetRelativePath(
                        packageDirectory,
                        source
                    );

                string destination =
                    Path.Combine(
                        packageDestination,
                        relative
                    );

                long length =
                    new FileInfo(source).Length;

                totalBytes =
                    checked(
                        totalBytes +
                        length
                    );

                jobs.Add(
                    new CopyJob(
                        source,
                        destination
                    )
                );
            }
        }
    }

    /// Copies every job with the requested worker count. Stops issuing new
    /// copies as soon as one fails (ParallelLoopState.Stop), because a
    /// partially copied export is going to be deleted anyway. Collects up to
    /// MaxReportedCopyFailures failures for the error message rather than
    /// flooding the console if many files fail at once.
    private void CopyJobs(
        List<CopyJob> jobs,
        int workers
    )
    {
        ConcurrentQueue<string> failures = [];

        Parallel.ForEach(
            jobs,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = workers
            },
            (
                job,
                state
            ) =>
            {
                if (state.IsStopped)
                {
                    return;
                }

                try
                {
                    copyEngine.Copy(
                        job.Source,
                        job.Destination
                    );
                }
                catch (Exception error)
                {
                    failures.Enqueue(
                        FormatCopyFailure(
                            job,
                            error
                        )
                    );

                    state.Stop();
                }
            }
        );

        if (failures.IsEmpty)
        {
            return;
        }

        string[] reported =
            failures
                .Take(
                    MaxReportedCopyFailures
                )
                .ToArray();

        int hidden =
            Math.Max(
                0,
                failures.Count -
                reported.Length
            );

        string message =
            "A driver file could not be copied. " +
            "The incomplete export will be removed." +
            Environment.NewLine +
            Environment.NewLine +
            string.Join(
                Environment.NewLine +
                Environment.NewLine,
                reported
            );

        if (hidden > 0)
        {
            message +=
                Environment.NewLine +
                Environment.NewLine +
                $"{hidden} additional copy failure(s) were not printed.";
        }

        throw new IOException(
            message
        );
    }

    private static string FormatCopyFailure(
        CopyJob job,
        Exception error
    )
    {
        string message =
            $"{error.GetType().Name}: {error.Message}" +
            Environment.NewLine +
            $"Source: {job.Source}" +
            Environment.NewLine +
            $"Destination: {job.Destination}";

        if (
            error is Win32Exception win32 &&
            win32.NativeErrorCode == 206
        )
        {
            message +=
                Environment.NewLine +
                "Hint: Windows rejected the path length. " +
                @"Try a shorter export path such as C:\Drivers.";
        }

        return message;
    }
}
