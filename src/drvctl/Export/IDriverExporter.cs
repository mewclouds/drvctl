namespace DrvCtl.Export;

/// The export pipeline's public seam - resolve, plan, and copy.
internal interface IDriverExporter
{
    /// Runs a full export: resolves published driver packages, builds a copy
    /// plan, copies into a staging directory, then atomically commits it to
    /// <see cref="ExportRequest.Destination"/>. Never calls DISM or verifies
    /// content - that is layered on by the caller based on validation mode.
    ExportResult Export(
        ExportRequest request
    );
}
