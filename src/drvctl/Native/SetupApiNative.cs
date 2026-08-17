using System.Runtime.InteropServices;

namespace DrvCtl.Native;

internal static partial class SetupApiNative
{
    internal static readonly nint InvalidInfHandle = -1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct InfContext
    {
        internal nint Inf;
        internal nint CurrentInf;
        internal uint Section;
        internal uint Line;
    }

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

    [DllImport("setupapi.dll", EntryPoint = "SetupVerifyInfFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupVerifyInfFile(string infName, nint altPlatformInfo, ref SpInfSignerInfoV2 infSignerInfo);
}
