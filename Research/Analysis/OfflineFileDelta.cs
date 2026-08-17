namespace DrvCtl.Analysis;

internal sealed record OfflineFileSnapshot(string Root, OfflineFileState[] Files);

internal sealed record OfflineFileState(string Path, long Size, string Sha256);

internal enum OfflineFileChange
{
    Added,
    Removed,
    Modified,
    Unchanged
}

internal sealed record OfflineFileDelta(
    OfflineFileChange Change,
    string Path,
    OfflineFileState? Before,
    OfflineFileState? After
);
