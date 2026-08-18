/*
 * A single open key within an offline registry hive. Every enumeration and
 * value read here follows the same Win32 buffer-growth pattern: call with a
 * guessed buffer size, and on ERROR_MORE_DATA retry with the size the API
 * reported.
 */

using System.Text;
using System.Buffers.Binary;
using DrvCtl.Native;

namespace DrvCtl.Registry;

/// An open key handle within an <see cref="OfflineRegistryHive"/>.
internal sealed class OfflineRegistryKey : IDisposable
{
    private const uint ErrorNoMoreItems = 259;
    private const uint ErrorMoreData = 234;
    private readonly SafeOfflineKeyHandle handle;
    internal OfflineRegistryKey(SafeOfflineKeyHandle handle) => this.handle = handle;

    internal OfflineRegistryKey OpenKey(string subKey) { uint result = OffregNative.OpenKey(handle, subKey, out nint nativeKey); OfflineRegistryHive.ThrowIfFailed(result, "OROpenKey"); return new OfflineRegistryKey(new SafeOfflineKeyHandle(nativeKey)); }
    internal OfflineRegistryKey CreateKey(string subKey) { uint result = OffregNative.CreateKey(handle, subKey, null, 0, 0, out nint nativeKey, out _); OfflineRegistryHive.ThrowIfFailed(result, "ORCreateKey"); return new OfflineRegistryKey(new SafeOfflineKeyHandle(nativeKey)); }

    internal unsafe IReadOnlyList<string> EnumerateSubKeyNames()
    {
        List<string> names = [];
        for (uint index = 0; ; index++)
        {
            int capacity = 256;
            while (true)
            {
                char[] buffer = new char[capacity]; uint length = (uint)buffer.Length;
                fixed (char* pointer = buffer)
                {
                    uint result = OffregNative.EnumKey(handle, index, pointer, ref length, null, null, 0);
                    if (result == ErrorNoMoreItems) return names;
                    if (result == ErrorMoreData) { capacity = checked((int)length + 1); continue; }
                    OfflineRegistryHive.ThrowIfFailed(result, "OREnumKey");
                }
                names.Add(new string(buffer, 0, checked((int)length))); break;
            }
        }
    }

    internal unsafe IReadOnlyList<OfflineRegistryValue> EnumerateValues()
    {
        List<OfflineRegistryValue> values = [];
        for (uint index = 0; ; index++)
        {
            int nameCapacity = 256, dataCapacity = 4096;
            while (true)
            {
                char[] name = new char[nameCapacity]; byte[] data = new byte[dataCapacity]; uint nameLength = (uint)name.Length, dataLength = (uint)data.Length;
                fixed (char* namePointer = name) fixed (byte* dataPointer = data)
                {
                    uint result = OffregNative.EnumValue(handle, index, namePointer, ref nameLength, out uint type, dataPointer, ref dataLength);
                    if (result == ErrorNoMoreItems) return values;
                    if (result == ErrorMoreData) { nameCapacity = Math.Max(nameCapacity * 2, checked((int)nameLength + 1)); dataCapacity = Math.Max(dataCapacity * 2, checked((int)dataLength)); continue; }
                    OfflineRegistryHive.ThrowIfFailed(result, "OREnumValue");
                    values.Add(new OfflineRegistryValue(new string(name, 0, checked((int)nameLength)), type, data[..checked((int)dataLength)]));
                }
                break;
            }
        }
    }

    internal unsafe OfflineRegistryValue ReadValue(string? name)
    {
        uint size = 0; uint result = OffregNative.GetValue(handle, null, name, out uint type, null, ref size);
        if (result != 0 && result != ErrorMoreData) OfflineRegistryHive.ThrowIfFailed(result, "ORGetValue");
        byte[] data = new byte[checked((int)size)];
        fixed (byte* pointer = data) { result = OffregNative.GetValue(handle, null, name, out type, pointer, ref size); OfflineRegistryHive.ThrowIfFailed(result, "ORGetValue"); }
        return new OfflineRegistryValue(name ?? string.Empty, type, data[..checked((int)size)]);
    }

    internal unsafe void SetValue(string? name, uint type, ReadOnlySpan<byte> data)
    {
        fixed (byte* pointer = data) OfflineRegistryHive.ThrowIfFailed(OffregNative.SetValue(handle, name, type, pointer, checked((uint)data.Length)), "ORSetValue");
    }

    internal void DeleteValue(string? name) => OfflineRegistryHive.ThrowIfFailed(OffregNative.DeleteValue(handle, name), "ORDeleteValue");

    internal void DeleteSubKey(string name) => OfflineRegistryHive.ThrowIfFailed(OffregNative.DeleteKey(handle, name), "ORDeleteKey");

    /// Recursively deletes a subkey and everything under it. offreg's
    /// ORDeleteKey only removes a leaf key, so children must be walked and
    /// deleted first.
    internal void DeleteSubKeyTree(string name)
    {
        using (OfflineRegistryKey child = OpenKey(name))
        {
            foreach (string subKey in child.EnumerateSubKeyNames()) child.DeleteSubKeyTree(subKey);
        }
        DeleteSubKey(name);
    }

    internal void SetString(string? name, string value, uint type = 1) => SetValue(name, type, Encoding.Unicode.GetBytes(value + '\0'));
    internal void SetDword(string name, int value)
    {
        Span<byte> data = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(data, value);
        SetValue(name, 4, data);
    }

    internal int ReadDword(string name)
    {
        OfflineRegistryValue value = ReadValue(name);
        if (value.Type != 4 || value.Data.Length != sizeof(int))
        {
            throw new InvalidDataException($"Offline registry value '{name}' is not a REG_DWORD.");
        }
        return BinaryPrimitives.ReadInt32LittleEndian(value.Data);
    }

    internal string ReadString(string name)
    {
        OfflineRegistryValue value = ReadValue(name);
        if (value.Type is not 1 and not 2)
        {
            throw new InvalidDataException($"Offline registry value '{name}' is not a string value.");
        }
        return Encoding.Unicode.GetString(value.Data).TrimEnd('\0');
    }
    public void Dispose() => handle.Dispose();
}

/// A registry value's raw form: name, REG_* type code, and undecoded bytes.
internal sealed record OfflineRegistryValue(string Name, uint Type, byte[] Data);
