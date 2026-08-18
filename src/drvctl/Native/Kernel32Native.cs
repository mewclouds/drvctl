/*
 * kernel32.dll P/Invoke declarations. All entry points here take wide
 * (UTF-16) strings, so every signature uses StringMarshalling.Utf16 to match
 * the *W export, never the ANSI *A one.
 */

using System.Runtime.InteropServices;

namespace DrvCtl.Native;

internal static partial class Kernel32Native
{
    /// Mirrors the native COPYFILE2_EXTENDED_PARAMETERS struct field for
    /// field. Size must be set to sizeof(CopyFile2ExtendedParameters) before
    /// the call, CopyFile2 uses it to detect struct-version mismatches.
    /// The callback/cancel fields are left null (nint 0) since drvctl copies
    /// synchronously and never cancels mid-copy.
    [StructLayout(LayoutKind.Sequential)]
    internal struct CopyFile2ExtendedParameters
    {
        internal uint Size;
        internal uint CopyFlags;
        internal nint Cancel;
        internal nint ProgressRoutine;
        internal nint CallbackContext;
    }

    /// CopyFile2 returns an HRESULT directly rather than using SetLastError,
    /// so failures are decoded from the return value (see
    /// CopyFile2Engine.HResultToWin32), not Marshal.GetLastPInvokeError.
    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "CopyFile2",
        StringMarshalling = StringMarshalling.Utf16
    )]
    internal static partial int CopyFile2(
        string existingFileName,
        string newFileName,
        in CopyFile2ExtendedParameters extendedParameters
    );

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "GetCurrentProcess"
    )]
    internal static partial nint GetCurrentProcess();

    /// nuint.MaxValue for both min and max is the documented way to tell
    /// Windows to drop the working-set-based cache limits entirely (used by
    /// CacheFlusher for cold-cache benchmarking). Requires
    /// SeIncreaseQuotaPrivilege, hence SetLastError to surface why it failed.
    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "SetSystemFileCacheSize",
        SetLastError = true
    )]
    internal static partial int SetSystemFileCacheSize(
        nuint minimumFileCacheSize,
        nuint maximumFileCacheSize,
        uint flags
    );

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "CloseHandle",
        SetLastError = true
    )]
    internal static partial int CloseHandle(
        nint handle
    );
}
