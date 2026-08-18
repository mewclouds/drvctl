/*
 * The comparison engine behind --verify, --full-verify, and --dism. Console
 * rendering is deliberately kept out of this file (see ConsoleOutput) so the
 * comparison logic can be tested and reasoned about without touching stdout.
 */

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

namespace DrvCtl.Verification;

/// How thoroughly two file trees are compared.
internal enum VerificationDepth
{
    /// File count, relative path, and size only. Backs --verify.
    Quick,

    /// Quick checks plus SHA-256. Backs --full-verify and --dism.
    Full
}

/// Builds fingerprint trees for two directories and compares them.
internal sealed class FileTreeVerifier
{
    private const int HashBufferBytes =
        1024 * 1024;

    /// Compares an export destination against the Driver Store packages it was
    /// copied from. This never touches DISM - it is a self-check of the copy.
    internal TreeComparisonResult CompareToSource(
        string destinationRoot,
        string[] packageDirectories,
        int workers,
        VerificationDepth depth
    )
    {
        Stopwatch watch =
            Stopwatch.StartNew();

        Dictionary<string, FileFingerprint> expected =
            BuildExpectedFingerprintTree(
                packageDirectories,
                workers,
                depth
            );

        Dictionary<string, FileFingerprint> actual =
            BuildFingerprintTree(
                destinationRoot,
                workers,
                depth
            );

        TreeComparisonResult result =
            CompareTrees(
                expected,
                actual,
                "source",
                "export",
                depth
            );

        watch.Stop();

        return result with { Seconds = watch.Elapsed.TotalSeconds };
    }

    /// Compares an export destination against a DISM reference export. Always
    /// runs the full (hashed) comparison, since the caller paid the DISM cost.
    internal TreeComparisonResult CompareToDism(
        string destinationRoot,
        string dismRoot,
        int workers
    )
    {
        Stopwatch watch =
            Stopwatch.StartNew();

        Dictionary<string, FileFingerprint> dism =
            BuildFingerprintTree(
                dismRoot,
                workers,
                VerificationDepth.Full
            );

        Dictionary<string, FileFingerprint> drvctl =
            BuildFingerprintTree(
                destinationRoot,
                workers,
                VerificationDepth.Full
            );

        TreeComparisonResult result =
            CompareTrees(
                dism,
                drvctl,
                "DISM",
                "drvctl",
                VerificationDepth.Full
            );

        watch.Stop();

        return result with { Seconds = watch.Elapsed.TotalSeconds };
    }

    /// Diffs two fingerprint trees by relative path. left/right and their
    /// labels are directional only for reporting ("missing from export"
    /// reads naturally either way), the comparison itself is symmetric.
    private static TreeComparisonResult CompareTrees(
        Dictionary<string, FileFingerprint> left,
        Dictionary<string, FileFingerprint> right,
        string leftLabel,
        string rightLabel,
        VerificationDepth depth
    )
    {
        int missingFromRight = 0;
        int missingFromLeft = 0;
        int sizeMismatches = 0;
        int hashMismatches = 0;

        List<string> differences = [];

        foreach (
            string relativePath in left.Keys.Order(
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            FileFingerprint leftFile =
                left[relativePath];

            if (
                !right.TryGetValue(
                    relativePath,
                    out FileFingerprint? rightFile
                ) ||
                rightFile is null
            )
            {
                missingFromRight++;

                differences.Add(
                    $"Missing from {rightLabel}: " +
                    relativePath
                );

                continue;
            }

            if (leftFile.Length != rightFile.Length)
            {
                sizeMismatches++;

                differences.Add(
                    "Size mismatch: " +
                    relativePath +
                    $" | {leftLabel}={leftFile.Length}" +
                    $" | {rightLabel}={rightFile.Length}"
                );

                continue;
            }

            if (
                depth == VerificationDepth.Full &&
                !string.Equals(
                    leftFile.Sha256,
                    rightFile.Sha256,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                hashMismatches++;

                differences.Add(
                    "SHA-256 mismatch: " +
                    relativePath +
                    $" | {leftLabel}={leftFile.Sha256}" +
                    $" | {rightLabel}={rightFile.Sha256}"
                );
            }
        }

        foreach (
            string relativePath in right.Keys.Order(
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            if (!left.ContainsKey(relativePath))
            {
                missingFromLeft++;

                differences.Add(
                    $"Missing from {leftLabel}: " +
                    relativePath
                );
            }
        }

        long leftBytes =
            left.Values.Sum(
                file => file.Length
            );

        long rightBytes =
            right.Values.Sum(
                file => file.Length
            );

        bool exactMatch =
            missingFromLeft == 0 &&
            missingFromRight == 0 &&
            sizeMismatches == 0 &&
            hashMismatches == 0;

        return new TreeComparisonResult(
            leftLabel,
            rightLabel,
            left.Count,
            right.Count,
            leftBytes,
            rightBytes,
            missingFromLeft,
            missingFromRight,
            sizeMismatches,
            hashMismatches,
            depth == VerificationDepth.Full,
            exactMatch,
            0,
            differences
        );
    }

    /// Builds the "expected" side of a source verification: one fingerprint
    /// per file across all Driver Store package directories, keyed by
    /// packageName/relativePath so it lines up with the export layout.
    private static Dictionary<string, FileFingerprint>
        BuildExpectedFingerprintTree(
            string[] packageDirectories,
            int workers,
            VerificationDepth depth
        )
    {
        List<(string RelativePath, string SourceFile)> entries = [];

        foreach (string packageDirectory in packageDirectories)
        {
            string packageName =
                new DirectoryInfo(packageDirectory).Name;

            foreach (
                string source in Directory.EnumerateFiles(
                    packageDirectory,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                string relative =
                    Path.Combine(
                        packageName,
                        Path.GetRelativePath(packageDirectory, source)
                    );

                entries.Add((relative, source));
            }
        }

        ConcurrentDictionary<string, FileFingerprint> fingerprints =
            new(StringComparer.OrdinalIgnoreCase);

        Parallel.ForEach(
            entries,
            new ParallelOptions { MaxDegreeOfParallelism = workers },
            entry =>
            {
                FileInfo info = new(entry.SourceFile);

                string? hash =
                    depth == VerificationDepth.Full
                        ? HashFile(entry.SourceFile)
                        : null;

                fingerprints[entry.RelativePath] =
                    new FileFingerprint(info.Length, hash);
            }
        );

        return new Dictionary<string, FileFingerprint>(
            fingerprints,
            StringComparer.OrdinalIgnoreCase
        );
    }

    /// Fingerprints every file under a single root directory (an export
    /// destination or a DISM reference export), keyed by relative path.
    private static Dictionary<string, FileFingerprint>
        BuildFingerprintTree(
            string root,
            int workers,
            VerificationDepth depth
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

                string? hash =
                    depth == VerificationDepth.Full
                        ? HashFile(file)
                        : null;

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

    /// Streams the file through SHA-256 with FileShare.ReadWrite so hashing
    /// an export never blocks a concurrent write elsewhere in the tree.
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

/// A file's size, plus its SHA-256 when the comparison depth requires it (null for Quick).
internal sealed record FileFingerprint(
    long Length,
    string? Sha256
);

/// The result of comparing two fingerprint trees.
internal sealed record TreeComparisonResult(
    string LeftLabel,
    string RightLabel,
    int LeftFiles,
    int RightFiles,
    long LeftBytes,
    long RightBytes,
    int MissingFromLeft,
    int MissingFromRight,
    int SizeMismatches,
    int HashMismatches,
    bool HashesCompared,
    bool ExactMatch,
    double Seconds,
    IReadOnlyList<string> Differences
);
