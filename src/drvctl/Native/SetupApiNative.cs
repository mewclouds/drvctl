/*
 * setupapi.dll declarations. This is the primary native surface for both the
 * public list/export commands (via DriverStoreResolver, which calls
 * SetupGetInfDriverStoreLocationW) and the research INF inspector
 * (InfInspector, which walks INF sections directly). All string-taking
 * entry points use the W (wide/UTF-16) export explicitly.
 */

using System.Runtime.InteropServices;

namespace DrvCtl.Native;

internal static partial class SetupApiNative
{
    /// SetupOpenInfFileW's documented invalid-handle sentinel. Not IntPtr.Zero.
    internal static readonly nint InvalidInfHandle = -1;

    /// Opaque cursor into an open INF's current line, produced and consumed
    /// by the SetupFindFirstLine/SetupFindNextLine family. Callers never read
    /// its fields directly, only pass it back to further SetupApi calls.
    [StructLayout(LayoutKind.Sequential)]
    internal struct InfContext
    {
        internal nint Inf;
        internal nint CurrentInf;
        internal uint Section;
        internal uint Line;
    }

    /// Mirrors SP_ALTPLATFORM_INFO_V2, used to force AMD64-specific section
    /// resolution regardless of what architecture drvctl itself is running
    /// on. Size must be set to sizeof(AlternatePlatformInfo) before the call,
    /// SetupAPI uses it to distinguish this from the older V1 struct shape.
    [StructLayout(LayoutKind.Sequential)]
    internal struct AlternatePlatformInfo
    {
        internal uint Size;
        internal uint Platform;
        internal uint MajorVersion;
        internal uint MinorVersion;
        internal ushort ProcessorArchitecture;
        internal ushort Reserved;
    }

    [LibraryImport("setupapi.dll", EntryPoint = "SetupOpenInfFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint SetupOpenInfFileW(string fileName, string? infClass, uint infStyle, out uint errorLine);

    [LibraryImport("setupapi.dll")]
    internal static partial void SetupCloseInfFile(nint infHandle);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupFindFirstLineW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupFindFirstLineW(nint infHandle, string section, string? key, out InfContext context);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupFindNextLine", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupFindNextLine(ref InfContext contextIn, out InfContext contextOut);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupFindNextMatchLineW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupFindNextMatchLineW(ref InfContext contextIn, string key, out InfContext contextOut);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupGetStringFieldW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool SetupGetStringFieldW(ref InfContext context, uint fieldIndex, char* returnBuffer, uint returnBufferSize, out uint requiredSize);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupGetIntField", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupGetIntField(ref InfContext context, uint fieldIndex, out int integerValue);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupGetFieldCount")]
    internal static partial uint SetupGetFieldCount(ref InfContext context);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetActualSectionToInstallExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool SetupDiGetActualSectionToInstallExW(nint infHandle, string infSectionName, nint alternatePlatformInfo, char* infSectionWithExt, uint infSectionWithExtSize, out uint requiredSize, out nint extension, nint reserved);

    /// The one entry point the public export/list surface depends on: maps a
    /// published OEM INF name to its backing Driver Store package INF path.
    /// Returns a Win32 BOOL-as-int (nonzero success), with the actual buffer
    /// growth handled by DriverStoreResolver.ResolveStoreInf.
    [LibraryImport(
        "setupapi.dll",
        EntryPoint = "SetupGetInfDriverStoreLocationW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16
    )]
    internal static unsafe partial int SetupGetInfDriverStoreLocationW(
        string fileName,
        nint alternatePlatformInfo,
        nint localeName,
        char* returnBuffer,
        uint returnBufferSize,
        out uint requiredSize
    );

    /// Mirrors SP_INF_SIGNER_INFO_V2. CbSize must be set before the call so
    /// SetupVerifyInfFile knows this is the V2 (not V1) struct shape. The
    /// three MAX_PATH (260) fixed-size string buffers are why this uses
    /// classic [DllImport]/[MarshalAs(ByValTStr)] below instead of
    /// [LibraryImport]: the source-generated marshaller does not support
    /// fixed-size inline string buffers.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SpInfSignerInfoV2
    {
        internal uint CbSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string CatalogFile;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string DigitalSigner;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string DigitalSignerVersion;
        internal uint SignerScore;
    }

    /// Reads the catalog and signer identity SetupAPI itself would use to
    /// validate the INF's signature. See SpInfSignerInfoV2 for why this stays on DllImport.
    [DllImport("setupapi.dll", EntryPoint = "SetupVerifyInfFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupVerifyInfFile(string infName, nint altPlatformInfo, ref SpInfSignerInfoV2 infSignerInfo);
}
