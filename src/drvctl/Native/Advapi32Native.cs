using System.Runtime.InteropServices;

namespace DrvCtl.Native;

internal static partial class Advapi32Native
{
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
