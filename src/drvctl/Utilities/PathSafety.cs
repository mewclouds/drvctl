/*
 * Guardrails for the one destructive input drvctl takes from the user: the
 * export destination path. Every check here exists because getting it wrong
 * would either destroy user data or corrupt the running Windows install.
 */

namespace DrvCtl.Utilities;

/// A validated, safe-to-use export destination.
internal sealed record DestinationPreflight(
    string Destination,
    string Parent,
    bool ExistedEmpty
);

internal static class PathSafety
{
    /// Validates and resolves an export destination path. Rejects filesystem
    /// roots, paths inside the Windows directory, existing files, and
    /// non-empty existing directories, then ensures the parent directory
    /// exists so a later Directory.Move to commit staging can succeed.
    /// <exception cref="InvalidOperationException">The destination fails any safety check.</exception>
    internal static DestinationPreflight ValidateExportDestination(
        string requestedPath
    )
    {
        string destination =
            Path.GetFullPath(
                requestedPath
            );

        string windowsDirectory =
            Path.GetFullPath(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.Windows
                )
            );

        string root =
            Path.GetPathRoot(destination)
            ?? throw new InvalidOperationException(
                $"Could not determine the filesystem root for '{destination}'."
            );

        if (
            PathsEqual(
                TrimEnd(destination),
                TrimEnd(root)
            )
        )
        {
            throw new InvalidOperationException(
                $"Refusing to use a filesystem root as the export destination: {destination}"
            );
        }

        if (
            IsSameOrBelow(
                destination,
                windowsDirectory
            )
        )
        {
            throw new InvalidOperationException(
                $"Refusing to export inside the Windows directory: {destination}"
            );
        }

        if (File.Exists(destination))
        {
            throw new InvalidOperationException(
                $"Export destination is an existing file: {destination}"
            );
        }

        bool existedEmpty =
            false;

        if (Directory.Exists(destination))
        {
            using IEnumerator<string> enumerator =
                Directory
                    .EnumerateFileSystemEntries(
                        destination
                    )
                    .GetEnumerator();

            if (enumerator.MoveNext())
            {
                throw new InvalidOperationException(
                    "Export destination already contains data. " +
                    $"Choose a new or empty directory: {destination}"
                );
            }

            existedEmpty =
                true;
        }

        string parent =
            Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException(
                $"Could not determine the parent directory for '{destination}'."
            );

        Directory.CreateDirectory(
            parent
        );

        return new DestinationPreflight(
            destination,
            parent,
            existedEmpty
        );
    }

    /// True if <paramref name="child"/> is <paramref name="parent"/> itself
    /// or a path underneath it, compared case-insensitively as Windows paths are.
    private static bool IsSameOrBelow(
        string child,
        string parent
    )
    {
        string normalizedChild =
            TrimEnd(
                Path.GetFullPath(child)
            );

        string normalizedParent =
            TrimEnd(
                Path.GetFullPath(parent)
            );

        if (
            PathsEqual(
                normalizedChild,
                normalizedParent
            )
        )
        {
            return true;
        }

        string prefix =
            normalizedParent +
            Path.DirectorySeparatorChar;

        return normalizedChild.StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool PathsEqual(
        string left,
        string right
    )
    {
        return string.Equals(
            left,
            right,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string TrimEnd(
        string value
    )
    {
        return value.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        );
    }
}
