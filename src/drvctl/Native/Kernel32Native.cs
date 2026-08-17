using System.Runtime.InteropServices;

namespace DrvCtl.Native;

internal static partial class Kernel32Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct CopyFile2ExtendedParameters
    {
        internal uint Size;
        internal uint CopyFlags;
        internal nint Cancel;
        internal nint ProgressRoutine;
        internal nint CallbackContext;
    }

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
