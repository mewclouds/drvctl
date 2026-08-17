namespace DrvCtl.Export;

internal interface IDriverExporter
{
    ExportResult Export(
        ExportRequest request
    );
}
