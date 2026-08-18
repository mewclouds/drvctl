/*
 * Seam between the export pipeline and the actual file-copy mechanism, so
 * the copy engine can be swapped or mocked without touching DriverExporter.
 */

namespace DrvCtl.Copy;

/// A single-file copy strategy used by the export pipeline.
internal interface ICopyEngine
{
    /// Display name surfaced in --verbose output (e.g. "Windows CopyFile2").
    string Name { get; }

    /// Copies <paramref name="source"/> to <paramref name="destination"/>.
    /// The destination's parent directory must already exist. Throws on failure.
    void Copy(
        string source,
        string destination
    );
}
