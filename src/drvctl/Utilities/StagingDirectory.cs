/*
 * Makes an export all-or-nothing. Files are copied into a hidden sibling
 * directory first, then Directory.Move commits it to the real destination in
 * one filesystem operation. If anything throws before Commit is called,
 * Dispose removes the partial copy so a failed export never leaves a
 * half-populated destination behind.
 */

namespace DrvCtl.Utilities;

/// A temporary directory next to the export destination, committed via an
/// atomic move or cleaned up on disposal if never committed.
internal sealed class StagingDirectory : IDisposable
{
    internal string Path { get; }

    private bool _committed;

    private StagingDirectory(
        string path
    )
    {
        Path = path;
    }

    /// Creates a hidden staging directory as a sibling of the future
    /// destination, so the final commit is a same-volume rename rather than
    /// a cross-volume copy.
    /// <exception cref="IOException">No unique name could be allocated after several attempts.</exception>
    internal static StagingDirectory Create(
        string parent
    )
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            string nonce =
                Guid.NewGuid()
                    .ToString("N")[..8];

            // Keep the sibling name short because deep Driver Store paths are real.
            string candidate =
                System.IO.Path.Combine(
                    parent,
                    ".drv-" + nonce
                );

            try
            {
                Directory.CreateDirectory(
                    candidate
                );

                return new StagingDirectory(
                    candidate
                );
            }
            catch (IOException)
            {
                if (Directory.Exists(candidate))
                {
                    continue;
                }

                throw;
            }
        }

        throw new IOException(
            "Could not allocate a unique staging directory after several attempts."
        );
    }

    /// Atomically moves the staged content to <paramref name="destination"/>.
    /// If the destination pre-existed as an empty directory it is removed
    /// first, since Directory.Move refuses to move onto an existing directory.
    internal void Commit(
        string destination,
        bool destinationExistedEmpty
    )
    {
        if (destinationExistedEmpty)
        {
            Directory.Delete(
                destination,
                recursive: false
            );
        }

        try
        {
            Directory.Move(
                Path,
                destination
            );

            _committed =
                true;
        }
        catch
        {
            if (
                destinationExistedEmpty &&
                !Directory.Exists(destination)
            )
            {
                try
                {
                    Directory.CreateDirectory(
                        destination
                    );
                }
                catch
                {
                    // Restoring the user's empty directory is best effort.
                }
            }

            throw;
        }
    }

    /// Removes the staging directory if Commit was never called. A no-op after a successful commit.
    public void Dispose()
    {
        if (
            _committed ||
            !Directory.Exists(Path)
        )
        {
            return;
        }

        Console.Error.WriteLine(
            $"Cleaning incomplete export: {Path}"
        );

        try
        {
            Directory.Delete(
                Path,
                recursive: true
            );
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                $"Warning: could not remove incomplete export '{Path}': {error.Message}"
            );
        }
    }
}
