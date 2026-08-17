namespace DrvCtl.Analysis;

internal sealed record OfflineRegistrySnapshot(
    string Hive,
    string RootPath,
    bool RootExists,
    OfflineRegistryKeyState[] Keys
);

internal sealed record OfflineRegistryKeyState(
    string Path,
    OfflineRegistryValueState[] Values
);

internal sealed record OfflineRegistryValueState(
    string KeyPath,
    string Name,
    uint Type,
    string TypeName,
    byte[] RawBytes,
    string RawHex,
    string? Decoded,
    string[]? DecodedStrings
);

internal enum OfflineRegistryChange
{
    KeyAdded,
    KeyRemoved,
    ValueAdded,
    ValueRemoved,
    ValueChanged
}

internal sealed record OfflineRegistryDelta(
    OfflineRegistryChange Change,
    string Hive,
    string KeyPath,
    string? ValueName,
    OfflineRegistryKeyState? BeforeKey,
    OfflineRegistryKeyState? AfterKey,
    OfflineRegistryValueState? BeforeValue,
    OfflineRegistryValueState? AfterValue
);
