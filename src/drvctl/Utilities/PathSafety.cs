namespace DrvCtl.Utilities;

internal sealed record DestinationPreflight(
    string Destination,
    string Parent,
    bool ExistedEmpty
);

internal static class PathSafety
{
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
