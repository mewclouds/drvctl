/*
 * advapi32.dll declarations for the process-token privilege adjustment
 * CacheFlusher needs to call SetSystemFileCacheSize.
 */

using System.Runtime.InteropServices;

namespace DrvCtl.Native;

internal static partial class Advapi32Native
{
    /// Mirrors the native LUID struct layout exactly (two 4-byte fields).
    /// Never constructed by hand, only ever produced by LookupPrivilegeValue.
    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        internal uint LowPart;
        internal int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LuidAndAttributes
    {
        internal Luid Luid;
        internal uint Attributes;
    }

    /// The real TOKEN_PRIVILEGES struct is variable-length (a PrivilegeCount
    /// followed by that many LuidAndAttributes entries), but drvctl only ever
    /// adjusts one privilege at a time, so this fixed single-entry layout is
    /// sufficient and avoids the marshalling complexity of a trailing array.
    [StructLayout(LayoutKind.Sequential)]
    internal struct TokenPrivileges
    {
        internal uint PrivilegeCount;
        internal LuidAndAttributes Privileges;
    }

    [LibraryImport(
        "advapi32.dll",
        EntryPoint = "OpenProcessToken",
        SetLastError = true
    )]
    internal static partial int OpenProcessToken(
        nint processHandle,
        uint desiredAccess,
        out nint tokenHandle
    );

    [LibraryImport(
        "advapi32.dll",
        EntryPoint = "LookupPrivilegeValueW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16
    )]
    internal static partial int LookupPrivilegeValue(
        string? systemName,
        string name,
        out Luid luid
    );

    [LibraryImport(
        "advapi32.dll",
        EntryPoint = "AdjustTokenPrivileges",
        SetLastError = true
    )]
    internal static partial int AdjustTokenPrivileges(
        nint tokenHandle,
        int disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        nint previousState,
        nint returnLength
    );
}
