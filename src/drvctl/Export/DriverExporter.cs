using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using DrvCtl.Copy;
using DrvCtl.Drivers;
using DrvCtl.Utilities;

namespace DrvCtl.Export;

internal sealed class DriverExporter(
    DriverStoreResolver resolver,
    ICopyEngine copyEngine
) : IDriverExporter
{
    private const int MaxReportedCopyFailures = 8;

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

        ConsoleOutput.PrintExportHeader(
            destination.Destination,
            resolution.PublishedInfCount,
            resolution.PackageDirectories.Length,
            request.Workers,
            copyEngine.Name
        );

        using StagingDirectory staging =
            StagingDirectory.Create(
                destination.Parent
            );

        Console.WriteLine();
        Console.WriteLine(
            "Exporting driver packages..."
        );

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
            endToEnd.Elapsed.TotalSeconds
        );
    }

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
