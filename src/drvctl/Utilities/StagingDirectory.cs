namespace DrvCtl.Utilities;

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
