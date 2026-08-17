namespace DrvCtl.Copy;

internal interface ICopyEngine
{
    string Name { get; }

    void Copy(
        string source,
        string destination
    );
}
