/*
 * Wraps the Windows Offline Registry Library (offreg.dll), which lets a
 * process read and write a hive file directly without loading it into the
 * live registry tree. Backs the hidden `simulate-apply` and
 * `analyze-publication` research commands. Not used by the public
 * export/list surface, which never touches the registry.
 */

using System.ComponentModel;
using DrvCtl.Native;

namespace DrvCtl.Registry;

/// A hive file opened offline via OROpenHive, independent of the live registry.
internal sealed class OfflineRegistryHive : IDisposable
{
    private readonly SafeOfflineHiveHandle handle;
    private OfflineRegistryHive(string path, SafeOfflineHiveHandle handle) { Path = path; this.handle = handle; }
    internal string Path { get; }

    /// <exception cref="Win32Exception">OROpenHive failed.</exception>
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

    /// Like OpenKey, but returns false instead of throwing when the subkey
    /// does not exist (offreg reports that as Win32 error 2, ERROR_FILE_NOT_FOUND).
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

    /// Writes the (possibly mutated) in-memory hive back out to a new hive file on disk.
    internal void Save(string path, uint osMajorVersion = 10, uint osMinorVersion = 0) => ThrowIfFailed(OffregNative.SaveHive(handle, System.IO.Path.GetFullPath(path), osMajorVersion, osMinorVersion), "ORSaveHive");
    public void Dispose() => handle.Dispose();

    /// Shared by OfflineRegistryHive and OfflineRegistryKey: offreg APIs
    /// return a Win32 error code directly instead of using SetLastError, so
    /// there is no Marshal.GetLastPInvokeError to read here.
    internal static void ThrowIfFailed(uint result, string operation) { if (result != 0) throw new Win32Exception(checked((int)result), $"{operation} failed"); }
}
