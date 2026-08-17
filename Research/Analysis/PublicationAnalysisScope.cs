namespace DrvCtl.Analysis;

internal static class PublicationAnalysisScope
{
    internal static readonly string[] WimPaths =
    [
        "/Windows/INF",
        "/Windows/System32/DriverStore/FileRepository",
        "/Windows/System32/CatRoot",
        "/Windows/System32/CatRoot2",
        "/Windows/System32/drivers",
        "/Windows/System32/config/SYSTEM",
        "/Windows/System32/config/SOFTWARE",
        "/Windows/System32/config/DRIVERS"
    ];

    internal static readonly (string Hive, string HiveRelativePath, string Root)[] RegistryRoots =
    [
        ("SYSTEM", @"Windows\System32\config\SYSTEM", @"DriverDatabase"),
        ("SYSTEM", @"Windows\System32\config\SYSTEM", @"ControlSet001\Services"),
        ("SOFTWARE", @"Windows\System32\config\SOFTWARE", @"Microsoft\Windows\CurrentVersion\Setup\PnpLockdownFiles"),
        ("SOFTWARE", @"Windows\System32\config\SOFTWARE", @"Microsoft\Windows\CurrentVersion\Setup\PnpResources"),
        ("DRIVERS", @"Windows\System32\config\DRIVERS", @"DriverDatabase")
    ];
}

