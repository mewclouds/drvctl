using System.Security.Principal;
using DrvCtl.Dism;

namespace DrvCtl.Verification;

/// Backs `drvctl export --dism`: creates a temporary DISM reference export,
/// compares it against an already-completed drvctl export, and cleans up.
internal sealed class DismComparisonRunner(
    DismRunner dism,
    FileTreeVerifier verifier
)
{
    /// Requires elevation (checked here, not left to DISM, so the failure
    /// message is a drvctl-authored one). Always removes the temporary DISM
    /// export in a finally block, even if the comparison itself throws.
    internal async Task<DismComparisonOutcome> RunAsync(
        string exportDestination,
        int workers
    )
    {
        if (!Elevation.IsAdministrator())
        {
            return DismComparisonOutcome.NotElevated();
        }

        string? parent =
            Path.GetDirectoryName(exportDestination);

        if (string.IsNullOrWhiteSpace(parent))
        {
            return DismComparisonOutcome.Failed(
                $"Could not determine a parent directory for '{exportDestination}'."
            );
        }

        string dismReference =
            CreateDismReferencePath(parent);

        try
        {
            Directory.CreateDirectory(dismReference);

            DismRunResult dismResult;

            try
            {
                dismResult =
                    await dism.ExportDriversAsync(dismReference);
            }
            catch (DismException error)
            {
                return DismComparisonOutcome.DismFailed(error);
            }

            TreeComparisonResult comparison =
                verifier.CompareToDism(
                    exportDestination,
                    dismReference,
                    workers
                );

            return DismComparisonOutcome.Completed(
                comparison,
                dismResult.Seconds
            );
        }
        finally
        {
            if (Directory.Exists(dismReference))
            {
                try
                {
                    Directory.Delete(dismReference, recursive: true);
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine(
                        $"Warning: could not remove temporary DISM reference '{dismReference}': {error.Message}"
                    );
                }
            }
        }
    }

    private static string CreateDismReferencePath(
        string parent
    )
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            string nonce =
                Guid.NewGuid()
                    .ToString("N")[..8];

            string candidate =
                Path.Combine(
                    parent,
                    ".dism-" + nonce
                );

            if (
                !Directory.Exists(candidate) &&
                !File.Exists(candidate)
            )
            {
                return candidate;
            }
        }

        throw new IOException(
            "Could not allocate a unique temporary DISM directory."
        );
    }

}

/// Checks the current process token for administrator membership.
internal static class Elevation
{
    /// True if the current process is running elevated.
    internal static bool IsAdministrator()
    {
        using WindowsIdentity identity =
            WindowsIdentity.GetCurrent();

        WindowsPrincipal principal =
            new(identity);

        return principal.IsInRole(
            WindowsBuiltInRole.Administrator
        );
    }
}

internal enum DismComparisonStatus
{
    Completed,
    NotElevated,
    DismFailed,
    Failed
}

internal sealed record DismComparisonOutcome(
    DismComparisonStatus Status,
    TreeComparisonResult? Comparison = null,
    double DismSeconds = 0,
    string? Message = null,
    DismException? DismError = null
)
{
    internal static DismComparisonOutcome Completed(
        TreeComparisonResult comparison,
        double dismSeconds
    ) => new(DismComparisonStatus.Completed, comparison, dismSeconds);

    internal static DismComparisonOutcome NotElevated() =>
        new(DismComparisonStatus.NotElevated);

    internal static DismComparisonOutcome DismFailed(
        DismException error
    ) => new(DismComparisonStatus.DismFailed, DismError: error, Message: error.Message);

    internal static DismComparisonOutcome Failed(
        string message
    ) => new(DismComparisonStatus.Failed, Message: message);
}
