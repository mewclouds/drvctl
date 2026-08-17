using System.ComponentModel;
using DrvCtl.Native;

namespace DrvCtl.Registry;

internal sealed class OfflineRegistryHive : IDisposable
{
    private readonly SafeOfflineHiveHandle handle;
    private OfflineRegistryHive(string path, SafeOfflineHiveHandle handle) { Path = path; this.handle = handle; }
    internal string Path { get; }

    internal static OfflineRegistryHive Open(string path)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        uint result = OffregNative.OpenHive(fullPath, out nint nativeHandle);
        ThrowIfFailed(result, "OROpenHive");
        SafeOfflineHiveHandle handle = new(nativeHandle);
        return new OfflineRegistryHive(fullPath, handle);
    }

    internal OfflineRegistryKey OpenKey(string subKey)
    {
        uint result = OffregNative.OpenKey(handle, subKey, out nint nativeKey);
        ThrowIfFailed(result, "OROpenKey");
        SafeOfflineKeyHandle key = new(nativeKey);
        return new OfflineRegistryKey(key);
    }

    internal bool TryOpenKey(string subKey, out OfflineRegistryKey? key)
    {
        uint result = OffregNative.OpenKey(handle, subKey, out nint nativeKey);
        if (result == 2)
        {
            key = null;
            return false;
        }
        ThrowIfFailed(result, "OROpenKey");
        key = new OfflineRegistryKey(new SafeOfflineKeyHandle(nativeKey));
        return true;
    }

    internal OfflineRegistryKey CreateKey(string subKey)
    {
        uint result = OffregNative.CreateKey(handle, subKey, null, 0, 0, out nint nativeKey, out _);
        ThrowIfFailed(result, "ORCreateKey");
        SafeOfflineKeyHandle key = new(nativeKey);
        return new OfflineRegistryKey(key);
    }

    internal void Save(string path, uint osMajorVersion = 10, uint osMinorVersion = 0) => ThrowIfFailed(OffregNative.SaveHive(handle, System.IO.Path.GetFullPath(path), osMajorVersion, osMinorVersion), "ORSaveHive");
    public void Dispose() => handle.Dispose();
    internal static void ThrowIfFailed(uint result, string operation) { if (result != 0) throw new Win32Exception(checked((int)result), $"{operation} failed"); }
}
