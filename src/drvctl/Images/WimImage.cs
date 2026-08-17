using System.Runtime.InteropServices;
using DrvCtl.Native;

namespace DrvCtl.Images;

internal sealed class WimImage : IDisposable
{
    private const int ExtractNoAcls = 0x00000040;
    private const int ExtractNoAttributes = 0x00100000;
    private readonly SafeWimHandle handle;

    private WimImage(string path, SafeWimHandle handle, WimlibNative.WimInfo info)
    {
        Path = path;
        this.handle = handle;
        ImageCount = checked((int)info.ImageCount);
        BootIndex = info.BootIndex;
        WimVersion = info.WimVersion;
        ChunkSize = info.ChunkSize;
        PartNumber = info.PartNumber;
        TotalParts = info.TotalParts;
        CompressionType = info.CompressionType;
        TotalBytes = info.TotalBytes;
    }

    internal string Path { get; }
    internal int ImageCount { get; }
    internal uint BootIndex { get; }
    internal uint WimVersion { get; }
    internal uint ChunkSize { get; }
    internal ushort PartNumber { get; }
    internal ushort TotalParts { get; }
    internal int CompressionType { get; }
    internal ulong TotalBytes { get; }

    internal static WimImage Open(string path)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        int result = WimlibNative.OpenWim(fullPath, 0, out nint nativeHandle);
        if (result != 0)
        {
            throw new InvalidOperationException($"wimlib_open_wim failed ({result}): {GetError(result)}");
        }

        SafeWimHandle handle = new(nativeHandle);

        result = WimlibNative.GetWimInfo(handle, out WimlibNative.WimInfo info);
        if (result != 0)
        {
            handle.Dispose();
            throw new InvalidOperationException($"wimlib_get_wim_info failed ({result}): {GetError(result)}");
        }

        return new WimImage(fullPath, handle, info);
    }

    internal WimImageMetadata InspectImage(int index)
    {
        ObjectDisposedException.ThrowIf(handle.IsClosed, this);
        if (index < 1 || index > ImageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Image index must be between 1 and {ImageCount}.");
        }

        return new WimImageMetadata(index, ReadString(WimlibNative.GetImageName(handle, index)), ReadString(WimlibNative.GetImageDescription(handle, index)));
    }

    internal unsafe void ExtractPaths(int index, string targetDirectory, IReadOnlyList<string> paths)
    {
        ObjectDisposedException.ThrowIf(handle.IsClosed, this);
        if (index < 1 || index > ImageCount) throw new ArgumentOutOfRangeException(nameof(index), index, $"Image index must be between 1 and {ImageCount}.");
        if (paths.Count == 0) throw new ArgumentException("At least one WIM path must be provided.", nameof(paths));
        string target = System.IO.Path.GetFullPath(targetDirectory);
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException($"WIM extraction target does not exist: {target}");

        nint nativeTarget = 0;
        nint nativePathList = 0;
        string pathList = System.IO.Path.Combine(target, ".drvctl-wim-paths.txt");
        try
        {
            File.WriteAllLines(pathList, paths, new System.Text.UTF8Encoding(false));
            nativeTarget = Marshal.StringToHGlobalUni(target);
            nativePathList = Marshal.StringToHGlobalUni(pathList);
            int result = WimlibNative.ExtractPathList(handle, index, nativeTarget, nativePathList, ExtractNoAcls | ExtractNoAttributes);
            if (result != 0) throw new InvalidOperationException($"wimlib_extract_pathlist failed ({result}): {GetError(result)}");
        }
        finally
        {
            if (nativeTarget != 0) Marshal.FreeHGlobal(nativeTarget);
            if (nativePathList != 0) Marshal.FreeHGlobal(nativePathList);
            File.Delete(pathList);
        }
    }

    internal void AddTree(int index, string fsSourcePath, string wimTargetPath, int addFlags = 0)
    {
        ObjectDisposedException.ThrowIf(handle.IsClosed, this);
        if (index < 1 || index > ImageCount) throw new ArgumentOutOfRangeException(nameof(index), index, $"Image index must be between 1 and {ImageCount}.");
        string source = System.IO.Path.GetFullPath(fsSourcePath);
        if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException($"Source path for WIM add does not exist: {source}", source);
        string target = wimTargetPath.StartsWith('\\') || wimTargetPath.StartsWith('/') ? wimTargetPath : "\\" + wimTargetPath;
        int result = WimlibNative.AddTree(handle, index, source, target, addFlags);
        if (result != 0) throw new InvalidOperationException($"wimlib_add_tree failed ({result}): {GetError(result)}");
    }

    internal void Overwrite(int writeFlags = 0, uint numThreads = 0)
    {
        ObjectDisposedException.ThrowIf(handle.IsClosed, this);
        int result = WimlibNative.Overwrite(handle, writeFlags, numThreads);
        if (result != 0) throw new InvalidOperationException($"wimlib_overwrite failed ({result}): {GetError(result)}");
    }

    public void Dispose() => handle.Dispose();

    private static string? ReadString(nint value) => value == 0 ? null : Marshal.PtrToStringUni(value);
    private static string GetError(int code) => ReadString(WimlibNative.GetErrorString(code)) ?? "Unknown wimlib error";
}

internal sealed record WimImageMetadata(int Index, string? Name, string? Description);
