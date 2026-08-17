using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DrvCtl.Native;

internal static partial class OffregNative
{
    [LibraryImport("offreg.dll", EntryPoint = "OROpenHive", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint OpenHive(string hivePath, out nint handle);
    [LibraryImport("offreg.dll", EntryPoint = "ORCloseHive")]
    internal static partial uint CloseHive(nint handle);
    [LibraryImport("offreg.dll", EntryPoint = "OROpenKey", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint OpenKey(SafeHandle handle, string subKey, out nint result);
    [LibraryImport("offreg.dll", EntryPoint = "ORCreateKey", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint CreateKey(SafeHandle handle, string subKey, string? keyClass, uint options, nint securityDescriptor, out nint result, out uint disposition);
    [LibraryImport("offreg.dll", EntryPoint = "ORCloseKey")]
    internal static partial uint CloseKey(nint handle);
    [LibraryImport("offreg.dll", EntryPoint = "OREnumKey")]
    internal static unsafe partial uint EnumKey(SafeHandle handle, uint index, char* name, ref uint nameLength, char* keyClass, uint* classLength, nint lastWriteTime);
    [LibraryImport("offreg.dll", EntryPoint = "OREnumValue")]
    internal static unsafe partial uint EnumValue(SafeHandle handle, uint index, char* valueName, ref uint valueNameLength, out uint type, byte* data, ref uint dataLength);
    [LibraryImport("offreg.dll", EntryPoint = "ORGetValue", StringMarshalling = StringMarshalling.Utf16)]
    internal static unsafe partial uint GetValue(SafeHandle handle, string? subKey, string? valueName, out uint type, byte* data, ref uint dataLength);
    [LibraryImport("offreg.dll", EntryPoint = "ORSetValue", StringMarshalling = StringMarshalling.Utf16)]
    internal static unsafe partial uint SetValue(SafeHandle handle, string? valueName, uint type, byte* data, uint dataLength);
    [LibraryImport("offreg.dll", EntryPoint = "ORDeleteValue", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint DeleteValue(SafeHandle handle, string? valueName);
    [LibraryImport("offreg.dll", EntryPoint = "ORDeleteKey", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint DeleteKey(SafeHandle handle, string subKey);
    [LibraryImport("offreg.dll", EntryPoint = "ORSaveHive", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint SaveHive(SafeOfflineHiveHandle handle, string path, uint osMajorVersion, uint osMinorVersion);
}

internal sealed class SafeOfflineHiveHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeOfflineHiveHandle() : base(true) { }
    internal SafeOfflineHiveHandle(nint handle) : base(true) => SetHandle(handle);
    protected override bool ReleaseHandle() => OffregNative.CloseHive(handle) == 0;
}

internal sealed class SafeOfflineKeyHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeOfflineKeyHandle() : base(true) { }
    internal SafeOfflineKeyHandle(nint handle) : base(true) => SetHandle(handle);
    protected override bool ReleaseHandle() => OffregNative.CloseKey(handle) == 0;
}
