/*
 * Process exit codes. Values are part of the CLI's external contract - a
 * script driving drvctl can branch on them - so treat them as append-only.
 */

namespace DrvCtl.Core;

/// Process exit codes returned by <c>drvctl</c>.
internal static class ExitCodes
{
    /// The command completed and, for validation modes, the comparison matched exactly.
    internal const int Success = 0;

    /// An unexpected failure occurred (I/O error, native call failure, etc).
    internal const int RuntimeFailure = 1;

    /// The command line could not be parsed.
    internal const int UsageFailure = 2;

    /// A verification or comparison ran successfully but found a mismatch.
    internal const int VerificationMismatch = 3;

    /// DISM itself failed or returned a non-zero exit code during <c>--dism</c>.
    internal const int DismFailure = 4;

    /// Reserved: unused by any current code path.
    internal const int ExportFailureDuringVerification = 5;
}
