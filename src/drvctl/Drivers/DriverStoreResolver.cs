using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using DrvCtl.Native;

namespace DrvCtl.Drivers;

internal sealed class DriverStoreResolver
{
    private const int InitialInfBufferChars = 512;
    private const int ErrorInsufficientBuffer = 122;

    internal DriverStoreResolution Resolve(
        int workers
    )
    {
        string windowsDirectory =
            Environment.GetFolderPath(
                Environment.SpecialFolder.Windows
            );

        string infDirectory =
            Path.Combine(
                windowsDirectory,
                "INF"
            );

        string[] publishedInfs =
            EnumeratePublishedOemInfs(
                infDirectory
            );

        if (publishedInfs.Length == 0)
        {
            throw new InvalidOperationException(
                $"No published third-party OEM INF files were found in '{infDirectory}'."
            );
        }

        ConcurrentBag<PublishedDriverPackage> resolved = [];
        ConcurrentBag<string> failures = [];

        Parallel.ForEach(
            publishedInfs,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = workers
            },
            publishedInf =>
            {
                try
                {
                    string storeInf =
                        ResolveStoreInf(
                            publishedInf
                        );

                    string? packageDirectory =
                        Path.GetDirectoryName(
                            storeInf
                        );

                    if (string.IsNullOrWhiteSpace(packageDirectory))
                    {
                        throw new InvalidOperationException(
                            "SetupAPI returned an INF without a package directory."
                        );
                    }

                    resolved.Add(
                        new PublishedDriverPackage(
                            Path.GetFileName(publishedInf),
                            storeInf,
                            packageDirectory
                        )
                    );
                }
                catch (Exception error)
                {
                    failures.Add(
                        $"{Path.GetFileName(publishedInf)}: {error.Message}"
                    );
                }
            }
        );

        if (!failures.IsEmpty)
        {
            string details =
                string.Join(
                    Environment.NewLine,
                    failures
                        .Order(
                            StringComparer.OrdinalIgnoreCase
                        )
                        .Select(
                            failure => $"  {failure}"
                        )
                );

            throw new InvalidOperationException(
                "Driver package resolution failed. " +
                "No export data was committed." +
                Environment.NewLine +
                details
            );
        }

        string[] uniquePackages =
            resolved
                .Select(mapping => mapping.PackageDirectory)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase
                )
                .Order(
                    StringComparer.OrdinalIgnoreCase
                )
                .ToArray();

        if (uniquePackages.Length == 0)
        {
            throw new InvalidOperationException(
                "SetupAPI resolved zero driver packages."
            );
        }

        return new DriverStoreResolution(
            resolved
                .OrderBy(
                    mapping => mapping.PublishedInfName,
                    StringComparer.OrdinalIgnoreCase
                )
                .ThenBy(
                    mapping => mapping.PublishedInfName,
                    StringComparer.Ordinal
                )
                .ToArray(),
            uniquePackages
        );
    }

    private static string[] EnumeratePublishedOemInfs(
        string infDirectory
    )
    {
        List<string> published = [];

        foreach (
            string file in Directory.EnumerateFiles(
                infDirectory,
                "oem*.inf",
                SearchOption.TopDirectoryOnly
            )
        )
        {
            if (
                IsPublishedOemInf(
                    Path.GetFileName(file)
                )
            )
            {
                published.Add(file);
            }
        }

        published.Sort(
            StringComparer.OrdinalIgnoreCase
        );

        return [.. published];
    }

    private static bool IsPublishedOemInf(
        string fileName
    )
    {
        if (
            fileName.Length < 8 ||
            !fileName.StartsWith(
                "oem",
                StringComparison.OrdinalIgnoreCase
            ) ||
            !fileName.EndsWith(
                ".inf",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return false;
        }

        ReadOnlySpan<char> digits =
            fileName.AsSpan(
                3,
                fileName.Length - 7
            );

        if (digits.IsEmpty)
        {
            return false;
        }

        foreach (char character in digits)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static unsafe string ResolveStoreInf(
        string publishedInf
    )
    {
        int capacity =
            InitialInfBufferChars;

        while (true)
        {
            char[] buffer =
                GC.AllocateUninitializedArray<char>(
                    capacity
                );

            uint requiredSize;
            int succeeded;

            fixed (char* returnBuffer = buffer)
            {
                succeeded =
                    SetupApiNative.SetupGetInfDriverStoreLocationW(
                        publishedInf,
                        0,
                        0,
                        returnBuffer,
                        (uint)buffer.Length,
                        out requiredSize
                    );
            }

            if (succeeded != 0)
            {
                int terminator =
                    Array.IndexOf(
                        buffer,
                        '\0'
                    );

                int length =
                    terminator >= 0
                        ? terminator
                        : buffer.Length;

                return new string(
                    buffer,
                    0,
                    length
                );
            }

            int error =
                Marshal.GetLastPInvokeError();

            if (
                error == ErrorInsufficientBuffer &&
                requiredSize > 0
            )
            {
                capacity =
                    Math.Max(
                        checked(
                            (int)requiredSize
                        ),
                        checked(
                            capacity * 2
                        )
                    );

                continue;
            }

            throw new Win32Exception(
                error,
                "SetupGetInfDriverStoreLocationW failed for " +
                publishedInf
            );
        }
    }
}

internal sealed record DriverStoreResolution(
    PublishedDriverPackage[] PublishedPackages,
    string[] PackageDirectories
)
{
    internal int PublishedInfCount => PublishedPackages.Length;
}

internal sealed record PublishedDriverPackage(
    string PublishedInfName,
    string StoreInfPath,
    string PackageDirectory
);
