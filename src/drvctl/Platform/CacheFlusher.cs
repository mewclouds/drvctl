/*
 * Cold-cache benchmarking utility. Not currently wired into any CLI command,
 * production or research. Kept for future benchmark work that needs a real
 * cold-cache baseline instead of the warm-cache caveat --dism --benchmark
 * already prints.
 */

using System.ComponentModel;
using System.Runtime.InteropServices;
using DrvCtl.Native;

namespace DrvCtl.Platform;

/// Drops the Windows system file cache via SetSystemFileCacheSize, requiring
/// and temporarily enabling SeIncreaseQuotaPrivilege to do so.
internal sealed class CacheFlusher
{
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;

    private const int ErrorNotAllAssigned = 1300;

    private const string IncreaseQuotaPrivilege =
        "SeIncreaseQuotaPrivilege";

    private const int SettleMilliseconds = 500;

    /// Requests SeIncreaseQuotaPrivilege, calls SetSystemFileCacheSize to
    /// evict the cache, then drops the privilege again. Requires an elevated
    /// process, since the privilege is not held by default even for admins.
    /// <exception cref="Win32Exception">Any step of the privilege or cache-size adjustment fails.</exception>
    internal void Flush()
    {
        nint token =
            0;

        Advapi32Native.Luid privilegeLuid =
            default;

        bool privilegeResolved =
            false;

        try
        {
            if (
                Advapi32Native.OpenProcessToken(
                    Kernel32Native.GetCurrentProcess(),
                    TokenAdjustPrivileges |
                    TokenQuery,
                    out token
                ) == 0
            )
            {
                throw LastWin32(
                    "OpenProcessToken failed."
                );
            }

            if (
                Advapi32Native.LookupPrivilegeValue(
                    null,
                    IncreaseQuotaPrivilege,
                    out privilegeLuid
                ) == 0
            )
            {
                throw LastWin32(
                    "LookupPrivilegeValue failed for SeIncreaseQuotaPrivilege."
                );
            }

            privilegeResolved =
                true;

            SetPrivilege(
                token,
                privilegeLuid,
                enabled: true
            );

            if (
                Kernel32Native.SetSystemFileCacheSize(
                    nuint.MaxValue,
                    nuint.MaxValue,
                    0
                ) == 0
            )
            {
                throw LastWin32(
                    "SetSystemFileCacheSize failed."
                );
            }
        }
        finally
        {
            if (
                token != 0 &&
                privilegeResolved
            )
            {
                try
                {
                    SetPrivilege(
                        token,
                        privilegeLuid,
                        enabled: false
                    );
                }
                catch
                {
                    // The cache request already finished. Privilege cleanup is best effort.
                }
            }

            if (token != 0)
            {
                Kernel32Native.CloseHandle(
                    token
                );
            }
        }

        Thread.Sleep(
            SettleMilliseconds
        );
    }

    private static void SetPrivilege(
        nint token,
        Advapi32Native.Luid luid,
        bool enabled
    )
    {
        Advapi32Native.TokenPrivileges state =
            new()
            {
                PrivilegeCount = 1,
                Privileges =
                    new Advapi32Native.LuidAndAttributes
                    {
                        Luid = luid,
                        Attributes =
                            enabled
                                ? SePrivilegeEnabled
                                : 0
                    }
            };

        int adjusted =
            Advapi32Native.AdjustTokenPrivileges(
                token,
                0,
                ref state,
                0,
                0,
                0
            );

        int error =
            Marshal.GetLastPInvokeError();

        if (adjusted == 0)
        {
            throw new Win32Exception(
                error,
                "AdjustTokenPrivileges failed."
            );
        }

        if (error == ErrorNotAllAssigned)
        {
            throw new Win32Exception(
                error,
                "The process token does not contain SeIncreaseQuotaPrivilege."
            );
        }
    }

    private static Win32Exception LastWin32(
        string message
    )
    {
        return new Win32Exception(
            Marshal.GetLastPInvokeError(),
            message
        );
    }
}
