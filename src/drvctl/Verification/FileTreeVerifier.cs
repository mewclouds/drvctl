using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

namespace DrvCtl.Verification;

internal sealed class FileTreeVerifier
{
    private const int HashBufferBytes =
        1024 * 1024;

    internal TreeComparisonResult Compare(
        string drvctlRoot,
        string dismRoot,
        int workers
    )
    {
        Stopwatch watch =
            Stopwatch.StartNew();

        Dictionary<string, FileFingerprint> drvctl =
            BuildFingerprintTree(
                drvctlRoot,
                workers
            );

        Dictionary<string, FileFingerprint> dism =
            BuildFingerprintTree(
                dismRoot,
                workers
            );

        int missingFromDrvCtl = 0;
        int missingFromDism = 0;
        int sizeMismatches = 0;
        int hashMismatches = 0;

        List<string> differences = [];

        foreach (
            string relativePath in drvctl.Keys.Order(
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            FileFingerprint drvctlFile =
                drvctl[relativePath];

            if (
                !dism.TryGetValue(
                    relativePath,
                    out FileFingerprint? dismFile
                ) ||
                dismFile is null
            )
            {
                missingFromDism++;

                differences.Add(
                    "Missing from DISM: " +
                    relativePath
                );

                continue;
            }

            if (
                drvctlFile.Length !=
                dismFile.Length
            )
            {
                sizeMismatches++;

                differences.Add(
                    "Size mismatch: " +
                    relativePath +
                    $" | drvctl={drvctlFile.Length}" +
                    $" | DISM={dismFile.Length}"
                );

                continue;
            }

            if (
                !string.Equals(
                    drvctlFile.Sha256,
                    dismFile.Sha256,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                hashMismatches++;

                differences.Add(
                    "SHA-256 mismatch: " +
                    relativePath +
                    $" | drvctl={drvctlFile.Sha256}" +
                    $" | DISM={dismFile.Sha256}"
                );
            }
        }

        foreach (
            string relativePath in dism.Keys.Order(
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            if (!drvctl.ContainsKey(relativePath))
            {
                missingFromDrvCtl++;

                differences.Add(
                    "Missing from drvctl: " +
                    relativePath
                );
            }
        }

        long drvctlBytes =
            drvctl.Values.Sum(
                file => file.Length
            );

        long dismBytes =
            dism.Values.Sum(
                file => file.Length
            );

        watch.Stop();

        bool exactMatch =
            missingFromDrvCtl == 0 &&
            missingFromDism == 0 &&
            sizeMismatches == 0 &&
            hashMismatches == 0;

        return new TreeComparisonResult(
            drvctl.Count,
            dism.Count,
            drvctlBytes,
            dismBytes,
            missingFromDrvCtl,
            missingFromDism,
            sizeMismatches,
            hashMismatches,
            exactMatch,
            watch.Elapsed.TotalSeconds,
            differences
        );
    }

    private static Dictionary<string, FileFingerprint>
        BuildFingerprintTree(
            string root,
            int workers
        )
    {
        string normalizedRoot =
            Path.GetFullPath(
                root
            );

        string[] files =
            Directory
                .EnumerateFiles(
                    normalizedRoot,
                    "*",
                    SearchOption.AllDirectories
                )
                .ToArray();

        ConcurrentDictionary<
            string,
            FileFingerprint
        > fingerprints =
            new(
                StringComparer.OrdinalIgnoreCase
            );

        Parallel.ForEach(
            files,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = workers
            },
            file =>
            {
                string relativePath =
                    Path.GetRelativePath(
                        normalizedRoot,
                        file
                    );

                FileInfo info =
                    new(
                        file
                    );

                string hash =
                    HashFile(
                        file
                    );

                fingerprints[relativePath] =
                    new FileFingerprint(
                        info.Length,
                        hash
                    );
            }
        );

        return new Dictionary<
            string,
            FileFingerprint
        >(
            fingerprints,
            StringComparer.OrdinalIgnoreCase
        );
    }

    private static string HashFile(
        string path
    )
    {
        using FileStream stream =
            new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite |
                FileShare.Delete,
                HashBufferBytes,
                FileOptions.SequentialScan
            );

        using SHA256 sha256 =
            SHA256.Create();

        byte[] hash =
            sha256.ComputeHash(
                stream
            );

        return Convert.ToHexString(
            hash
        );
    }
}

internal sealed record FileFingerprint(
    long Length,
    string Sha256
);

internal sealed record TreeComparisonResult(
    int DrvCtlFiles,
    int DismFiles,
    long DrvCtlBytes,
    long DismBytes,
    int MissingFromDrvCtl,
    int MissingFromDism,
    int SizeMismatches,
    int HashMismatches,
    bool ExactMatch,
    double Seconds,
    IReadOnlyList<string> Differences
);
