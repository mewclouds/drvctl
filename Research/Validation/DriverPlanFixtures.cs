namespace DrvCtl.Validation;

internal static class DriverPlanFixtures
{
    internal static readonly DriverPlanFixture[] All =
    [
        new(
            "ACPIVPC",
            "acpivpc.inf_amd64_fd0a5766a43dadc1",
            ["AcpiVpc.cat", "acpivpc.inf", "AcpiVpc.sys"],
            [@"ACPI\VEN_VPC&DEV_2004"],
            [new("AcpiVpc.sys", @"Windows\System32\drivers\AcpiVpc.sys")],
            [new("ACPIVPC", 1, 3, 1, @"\SystemRoot\System32\drivers\AcpiVpc.sys")],
            [
                new("Published INF allocation", ObservedServicingField.PublishedInfIdentity, "oem0.inf"),
                new("Catalog publication", ObservedServicingField.CatalogPublication, @"Windows\System32\CatRoot\{F750E6C3-38EE-11D1-85E5-00C04FC295EE}\oem0.cat"),
                new("FileRepository identity", ObservedServicingField.FileRepositoryIdentity, "acpivpc.inf_amd64_fd0a5766a43dadc1"),
                new("DriverDatabase hive selection", ObservedServicingField.DriverDatabaseHive, "SYSTEM"),
                new("DriverDatabase representation", ObservedServicingField.DriverDatabaseRepresentation, "Package present under DriverDatabase"),
                new("Service owner", ObservedServicingField.OwnershipMetadata, "oem0.inf"),
                new("PnP lockdown ownership", ObservedServicingField.OwnershipMetadata, @"%SystemRoot%\System32\drivers\AcpiVpc.sys"),
                new("Reflected file byte identity", ObservedServicingField.ReflectedFileByteIdentity, "Byte-identical to package AcpiVpc.sys")
            ]),
        new(
            "RzS4LWI",
            "rzs4lwi_0a58.inf_amd64_aecac1c0c5a62538",
            ["RzS4LWI_0A58.cat", "RzS4LWI_0A58.inf", "RzS4LWIStub.exe", "RzS4WizardPkgS4.exe"],
            [@"SWC\VID_RAZER&Razer_LWIWizard_0A58"],
            [],
            [],
            [
                new("DriverDatabase hive selection", ObservedServicingField.DriverDatabaseHive, "DRIVERS"),
                new("DriverDatabase representation", ObservedServicingField.DriverDatabaseRepresentation, "Package present under DriverDatabase"),
                new("No logical SOFTWARE registry delta", ObservedServicingField.SoftwareRegistryDelta, "No delta")
            ]),
        new(
            "RzS4Ext",
            "rzs4ext_0a58.inf_amd64_dc5be97a64b0151a",
            ["RzS4Ext_0A58.cat", "RzS4Ext_0A58.inf"],
            [@"USB\VID_1532&PID_0A58&MI_05"],
            [],
            [],
            [
                new("DriverDatabase hive selection", ObservedServicingField.DriverDatabaseHive, "SYSTEM"),
                new("DriverDatabase representation", ObservedServicingField.DriverDatabaseRepresentation, "Package present under DriverDatabase")
            ])
    ];

    internal static DriverPlanFixture FindForPackage(string packageDirectory)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageDirectory)));
        return All.SingleOrDefault(fixture => fixture.PackageDirectoryName.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No semantic validation fixture is registered for package directory '{name}'.");
    }
}
