/*
 * Answers the question both `export` and `list` are built on: which
 * third-party driver packages has Windows published, and where do they
 * actually live in the Driver Store. Uses SetupAPI directly rather than
 * shelling out to pnputil or DISM, since this is a hot path drvctl calls on
 * every invocation.
 */

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using DrvCtl.Native;

namespace DrvCtl.Drivers;

/// Resolves published OEM INF files (%WINDIR%\INF\oemNN.inf) to their
/// backing Driver Store packages.
internal sealed class DriverStoreResolver
{
    private const int InitialInfBufferChars = 512;
    private const int ErrorInsufficientBuffer = 122;

    /// Enumerates every published OEM INF and resolves each to its Driver
    /// Store package directory via SetupGetInfDriverStoreLocationW, in
    /// parallel across <paramref name="workers"/> threads. When
    /// <paramref name="includeIdentity"/> is set, also reads the identity
    /// fields (Provider/Class/Version/etc) needed for friendly `list` output,
    /// best-effort: a package still resolves even if identity reading fails.
    /// <exception cref="InvalidOperationException">
    /// No OEM INFs were published, resolution failed for one or more of
    /// them, or SetupAPI resolved zero packages.
    /// </exception>
    internal DriverStoreResolution Resolve(
        int workers,
        bool includeIdentity = false
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

                    InfIdentity? identity = null;

                    if (includeIdentity)
                    {
                        try
                        {
                            identity =
                                new InfInspector().InspectIdentity(
                                    storeInf
                                );
                        }
                        catch
                        {
                            // Identity fields are a friendly-output nicety, not a resolution
                            // requirement. A package still resolves without them.
                        }
                    }

                    resolved.Add(
                        new PublishedDriverPackage(
                            Path.GetFileName(publishedInf),
                            storeInf,
                            packageDirectory,
                            identity?.Provider,
                            identity?.Class,
                            identity?.ClassGuid,
                            identity?.DriverDate,
                            identity?.DriverVersion,
                            identity?.CatalogFile
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

    /// Lists %WINDIR%\INF\oem*.inf and filters to the ones that actually
    /// match the published-INF naming pattern (oem &lt;digits&gt; .inf), since
    /// the directory can contain other oem*.inf files that are not published driver INFs.
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

    /// True for "oemNN.inf" (digits only between "oem" and ".inf"), false
    /// for anything else Windows might also name oem*.inf.
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

    /// Calls SetupGetInfDriverStoreLocationW with the standard Win32
    /// grow-and-retry buffer pattern: try a starting buffer size, and if the
    /// API reports ERROR_INSUFFICIENT_BUFFER, retry with the size it reported
    /// (or double the previous attempt, whichever is larger).
    /// <exception cref="Win32Exception">SetupGetInfDriverStoreLocationW failed for a reason other than a too-small buffer.</exception>
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

/// All published INFs and the distinct Driver Store package directories they resolved to.
/// A single package directory can back more than one published INF, which is why
/// PublishedInfCount and PackageDirectories.Length can differ.
internal sealed record DriverStoreResolution(
    PublishedDriverPackage[] PublishedPackages,
    string[] PackageDirectories
)
{
    internal int PublishedInfCount => PublishedPackages.Length;
}

/// One published OEM INF and, when requested, its identity fields.
internal sealed record PublishedDriverPackage(
    string PublishedInfName,
    string StoreInfPath,
    string PackageDirectory,
    string? Provider = null,
    string? Class = null,
    string? ClassGuid = null,
    string? DriverDate = null,
    string? DriverVersion = null,
    string? CatalogFile = null
);
