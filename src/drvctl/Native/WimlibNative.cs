/*
 * libwim (wimlib) declarations. The library binary (native\libwim-15.dll)
 * ships alongside drvctl rather than relying on anything already present on
 * the target machine, since WIM manipulation is research-only functionality
 * with no guaranteed system-provided equivalent.
 */

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DrvCtl.Native;

internal static partial class WimlibNative
{
    internal const string LibraryName = "libwim-15.dll";

    /// Mirrors struct wimlib_wim_info. The trailing Reserved array pads the
    /// struct to match wimlib's ABI, callers never read it.
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

/// SafeHandle around an open wimlib WIMStruct pointer. wimlib_free has no
/// failure return, so ReleaseHandle always reports success.
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
