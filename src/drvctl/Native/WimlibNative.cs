using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DrvCtl.Native;

internal static partial class WimlibNative
{
    internal const string LibraryName = "libwim-15.dll";

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct WimInfo
    {
        internal fixed byte Guid[16];
        internal uint ImageCount;
        internal uint BootIndex;
        internal uint WimVersion;
        internal uint ChunkSize;
        internal ushort PartNumber;
        internal ushort TotalParts;
        internal int CompressionType;
        internal ulong TotalBytes;
        internal uint Flags;
        internal fixed uint Reserved[9];
    }

    [LibraryImport(LibraryName, EntryPoint = "wimlib_open_wim", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int OpenWim(string path, int openFlags, out nint handle);

    [LibraryImport(LibraryName, EntryPoint = "wimlib_free")]
    internal static partial void Free(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "wimlib_get_wim_info")]
    internal static partial int GetWimInfo(SafeWimHandle handle, out WimInfo info);

    [LibraryImport(LibraryName, EntryPoint = "wimlib_get_image_name")]
    internal static partial nint GetImageName(SafeWimHandle handle, int image);

    [LibraryImport(LibraryName, EntryPoint = "wimlib_get_image_description")]
    internal static partial nint GetImageDescription(SafeWimHandle handle, int image);

    [LibraryImport(LibraryName, EntryPoint = "wimlib_get_error_string")]
    internal static partial nint GetErrorString(int errorCode);

    [LibraryImport(LibraryName, EntryPoint = "wimlib_extract_pathlist")]
    internal static partial int ExtractPathList(SafeWimHandle handle, int image, nint target, nint pathListFile, int extractFlags);

    [LibraryImport(LibraryName, EntryPoint = "wimlib_add_tree", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int AddTree(SafeWimHandle handle, int image, string fsSourcePath, string wimTargetPath, int addFlags);

    [LibraryImport(LibraryName, EntryPoint = "wimlib_overwrite")]
    internal static partial int Overwrite(SafeWimHandle handle, int writeFlags, uint numThreads);
}

internal sealed class SafeWimHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeWimHandle() : base(true) { }
    internal SafeWimHandle(nint handle) : base(true) => SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        WimlibNative.Free(handle);
        return true;
    }
}
