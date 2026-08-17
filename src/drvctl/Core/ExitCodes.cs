namespace DrvCtl.Core;

internal static class ExitCodes
{
    internal const int Success = 0;
    internal const int RuntimeFailure = 1;
    internal const int UsageFailure = 2;
    internal const int VerificationMismatch = 3;
    internal const int DismFailure = 4;
    internal const int ExportFailureDuringVerification = 5;
}
