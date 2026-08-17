using System.ComponentModel;
using System.Runtime.InteropServices;
using DrvCtl.Native;

namespace DrvCtl.Drivers;

internal sealed class InfInspector
{
    private const uint InfStyleWin4 = 2;
    private const int ErrorInsufficientBuffer = 122;
    private const uint PlatformWin32Nt = 2;
    private const ushort ProcessorArchitectureAmd64 = 9;

    internal InfInspection Inspect(string path)
    {
        string fullPath = Path.GetFullPath(path);
        nint inf = SetupApiNative.SetupOpenInfFileW(fullPath, null, InfStyleWin4, out uint errorLine);
        if (inf == SetupApiNative.InvalidInfHandle)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"SetupOpenInfFileW failed at INF line {errorLine}");
        }

        try
        {
            string? modelsBase = null;
            string? modelsSection = null;
            string? manufacturer = null;
            if (SetupApiNative.SetupFindFirstLineW(inf, "Manufacturer", null, out SetupApiNative.InfContext manufacturerLine))
            {
                manufacturer = ReadField(ref manufacturerLine, 0);
                modelsBase = ReadField(ref manufacturerLine, 1);
                uint fieldCount = SetupApiNative.SetupGetFieldCount(ref manufacturerLine);
                for (uint field = 2; field <= fieldCount; field++)
                {
                    string? decoration = ReadField(ref manufacturerLine, field);
                    if (decoration is not null && decoration.StartsWith("NTamd64", StringComparison.OrdinalIgnoreCase))
                    {
                        modelsSection = modelsBase + "." + decoration;
                        break;
                    }
                }
            }
            List<string> installSections = [];
            List<string> hardwareIds = [];
            List<InfModelEntry> models = [];

            if (modelsSection is not null && SetupApiNative.SetupFindFirstLineW(inf, modelsSection, null, out SetupApiNative.InfContext modelLine))
            {
                do
                {
                    string? installBase = ReadField(ref modelLine, 1);
                    string? description = ReadField(ref modelLine, 0);
                    List<string> modelIds = [];
                    if (installBase is not null)
                    {
                        string actual = GetActualSection(inf, installBase);
                        if (!installSections.Contains(actual, StringComparer.OrdinalIgnoreCase)) installSections.Add(actual);
                    }

                    uint fields = SetupApiNative.SetupGetFieldCount(ref modelLine);
                    for (uint field = 2; field <= fields; field++)
                    {
                        string? id = ReadField(ref modelLine, field);
                        if (!string.IsNullOrWhiteSpace(id)) { hardwareIds.Add(id); modelIds.Add(id); }
                    }
                    if (!string.IsNullOrWhiteSpace(description) && installBase is not null)
                        models.Add(new InfModelEntry(description, GetActualSection(inf, installBase), [.. modelIds], manufacturer ?? string.Empty));
                }
                while (MoveNext(ref modelLine));
            }

            string[] copyFiles = installSections.SelectMany(section => ReadMatchingFields(inf, section, "CopyFiles")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string[] addServices = installSections.SelectMany(section => ReadMatchingFields(inf, section + ".Services", "AddService")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            InfCopyOperation[] copyOperations = installSections.SelectMany(section => ReadCopyOperations(inf, section)).ToArray();
            InfServiceOperation[] serviceOperations = installSections.SelectMany(section => ReadServiceOperations(inf, section)).ToArray();
            bool hasAddSoftware = installSections.Any(section => SetupApiNative.SetupFindFirstLineW(inf, section + ".Software", "AddSoftware", out _));
            string[] componentIds = installSections.SelectMany(section => ReadReferencedValues(inf, section + ".Components", "AddComponent", 3, "ComponentIds")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            InfStringValue[] strings = ReadStrings(inf);

            InfSignatureInfo? signature = null;
            SetupApiNative.SpInfSignerInfoV2 signerInfo = new() { CbSize = (uint)Marshal.SizeOf<SetupApiNative.SpInfSignerInfoV2>() };
            if (SetupApiNative.SetupVerifyInfFile(fullPath, 0, ref signerInfo))
            {
                signature = new InfSignatureInfo(
                    string.IsNullOrWhiteSpace(signerInfo.CatalogFile) ? null : signerInfo.CatalogFile,
                    string.IsNullOrWhiteSpace(signerInfo.DigitalSigner) ? null : signerInfo.DigitalSigner,
                    string.IsNullOrWhiteSpace(signerInfo.DigitalSignerVersion) ? null : signerInfo.DigitalSignerVersion,
                    signerInfo.SignerScore);
            }

            return new InfInspection(fullPath, ReadFirstField(inf, "Version", "Class", 1), ReadFirstField(inf, "Version", "ClassGuid", 1), ReadFirstField(inf, "Version", "Provider", 1), ReadFirstField(inf, "Version", "CatalogFile", 1), ReadDriverVersion(inf), modelsSection, [.. installSections], copyFiles, addServices, [.. hardwareIds.Distinct(StringComparer.OrdinalIgnoreCase)], copyOperations, serviceOperations, ReadFirstField(inf, "Version", "ExtensionId", 1), hasAddSoftware, componentIds, [.. models], strings, ReadOptionalIntValue(inf, "Version", "PnpLockdown"), signature);
        }
        finally { SetupApiNative.SetupCloseInfFile(inf); }
    }

    private static InfStringValue[] ReadStrings(nint inf)
    {
        if (!SetupApiNative.SetupFindFirstLineW(inf, "Strings", null, out SetupApiNative.InfContext context)) return [];
        List<InfStringValue> values = [];
        do
        {
            string? name = ReadField(ref context, 0);
            string? value = ReadField(ref context, 1);
            if (!string.IsNullOrWhiteSpace(name) && value is not null) values.Add(new InfStringValue(name, value));
        }
        while (MoveNext(ref context));
        return [.. values];
    }

    private static IEnumerable<string> ReadReferencedValues(nint inf, string section, string directive, uint referenceField, string referencedKey)
    {
        if (!SetupApiNative.SetupFindFirstLineW(inf, section, directive, out SetupApiNative.InfContext context)) yield break;
        while (true)
        {
            string? referencedSection = ReadField(ref context, referenceField);
            if (!string.IsNullOrWhiteSpace(referencedSection) && SetupApiNative.SetupFindFirstLineW(inf, referencedSection, referencedKey, out SetupApiNative.InfContext valueContext))
            {
                uint count = SetupApiNative.SetupGetFieldCount(ref valueContext);
                for (uint field = 1; field <= count; field++)
                {
                    string? value = ReadField(ref valueContext, field);
                    if (!string.IsNullOrWhiteSpace(value)) yield return value;
                }
            }
            SetupApiNative.InfContext current = context;
            if (!SetupApiNative.SetupFindNextMatchLineW(ref current, directive, out context)) break;
        }
    }

    private static IEnumerable<InfCopyOperation> ReadCopyOperations(nint inf, string installSection)
    {
        if (!SetupApiNative.SetupFindFirstLineW(inf, installSection, "CopyFiles", out SetupApiNative.InfContext context)) yield break;
        while (true)
        {
            uint count = SetupApiNative.SetupGetFieldCount(ref context);
            for (uint field = 1; field <= count; field++)
            {
                string? directive = ReadField(ref context, field);
                if (string.IsNullOrWhiteSpace(directive)) continue;
                if (directive[0] == '@')
                {
                    (int directoryId, string? subdirectory) = ReadDestination(inf, null);
                    string file = directive[1..];
                    yield return new InfCopyOperation(installSection, file, file, directoryId, subdirectory);
                    continue;
                }

                (int sectionDirectoryId, string? sectionSubdirectory) = ReadDestination(inf, directive);
                if (!SetupApiNative.SetupFindFirstLineW(inf, directive, null, out SetupApiNative.InfContext fileLine)) continue;
                do
                {
                    string? destinationFile = ReadField(ref fileLine, 0);
                    if (string.IsNullOrWhiteSpace(destinationFile)) continue;
                    string sourceFile = ReadField(ref fileLine, 1) ?? destinationFile;
                    yield return new InfCopyOperation(installSection, sourceFile, destinationFile, sectionDirectoryId, sectionSubdirectory);
                }
                while (MoveNext(ref fileLine));
            }
            SetupApiNative.InfContext current = context;
            if (!SetupApiNative.SetupFindNextMatchLineW(ref current, "CopyFiles", out context)) break;
        }
    }

    private static (int DirectoryId, string? Subdirectory) ReadDestination(nint inf, string? copySection)
    {
        string key = copySection ?? "DefaultDestDir";
        if (!SetupApiNative.SetupFindFirstLineW(inf, "DestinationDirs", key, out SetupApiNative.InfContext context) &&
            (copySection is null || !SetupApiNative.SetupFindFirstLineW(inf, "DestinationDirs", "DefaultDestDir", out context)))
        {
            throw new InvalidOperationException($"No DestinationDirs entry applies to '{copySection ?? "direct CopyFiles directive"}'.");
        }
        int directoryId = ReadIntField(ref context, 1, $"DestinationDirs/{key}");
        return (directoryId, ReadField(ref context, 2));
    }

    private static IEnumerable<InfServiceOperation> ReadServiceOperations(nint inf, string installSection)
    {
        string servicesSection = installSection + ".Services";
        if (!SetupApiNative.SetupFindFirstLineW(inf, servicesSection, "AddService", out SetupApiNative.InfContext context)) yield break;
        while (true)
        {
            string? name = ReadField(ref context, 1);
            string? configurationSection = ReadField(ref context, 3);
            int flags = ReadIntField(ref context, 2, servicesSection + "/AddService");
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(configurationSection))
            {
                SetupApiNative.InfContext emptyCurrent = context;
                if (!SetupApiNative.SetupFindNextMatchLineW(ref emptyCurrent, "AddService", out context)) break;
                continue;
            }
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(configurationSection))
            {
                throw new InvalidOperationException($"AddService in '{servicesSection}' is missing its service name or configuration section.");
            }
            int type = ReadRequiredIntValue(inf, configurationSection, "ServiceType");
            int start = ReadRequiredIntValue(inf, configurationSection, "StartType");
            int errorControl = ReadRequiredIntValue(inf, configurationSection, "ErrorControl");
            string serviceBinary = ReadFirstField(inf, configurationSection, "ServiceBinary", 1)
                ?? throw new InvalidOperationException($"Service configuration section '{configurationSection}' has no ServiceBinary value.");
            string? displayName = ReadFirstField(inf, configurationSection, "DisplayName", 1);
            yield return new InfServiceOperation(installSection, servicesSection, configurationSection, name, flags, type, start, errorControl, serviceBinary, displayName);

            SetupApiNative.InfContext current = context;
            if (!SetupApiNative.SetupFindNextMatchLineW(ref current, "AddService", out context)) break;
        }
    }

    private static int ReadRequiredIntValue(nint inf, string section, string key)
    {
        if (!SetupApiNative.SetupFindFirstLineW(inf, section, key, out SetupApiNative.InfContext context))
        {
            throw new InvalidOperationException($"Required value '{key}' was not found in section '{section}'.");
        }
        return ReadIntField(ref context, 1, section + "/" + key);
    }

    private static int? ReadOptionalIntValue(nint inf, string section, string key)
    {
        if (!SetupApiNative.SetupFindFirstLineW(inf, section, key, out SetupApiNative.InfContext context)) return null;
        return ReadIntField(ref context, 1, section + "/" + key);
    }

    private static int ReadIntField(ref SetupApiNative.InfContext context, uint field, string description)
    {
        if (!SetupApiNative.SetupGetIntField(ref context, field, out int value))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"SetupGetIntField failed for '{description}'.");
        }
        return value;
    }

    private static string[] ReadMatchingFields(nint inf, string section, string key)
    {
        List<string> values = [];
        if (!SetupApiNative.SetupFindFirstLineW(inf, section, key, out SetupApiNative.InfContext context)) return [];
        while (true)
        {
            uint count = SetupApiNative.SetupGetFieldCount(ref context);
            for (uint field = 1; field <= count; field++) { string? value = ReadField(ref context, field); if (!string.IsNullOrWhiteSpace(value)) values.Add(value); }
            SetupApiNative.InfContext current = context;
            if (!SetupApiNative.SetupFindNextMatchLineW(ref current, key, out context)) break;
        }
        return [.. values];
    }

    private static string? ReadFirstField(nint inf, string section, string? key, uint field)
    {
        return SetupApiNative.SetupFindFirstLineW(inf, section, key, out SetupApiNative.InfContext context) ? ReadField(ref context, field) : null;
    }

    private static string? ReadDriverVersion(nint inf)
    {
        if (!SetupApiNative.SetupFindFirstLineW(inf, "Version", "DriverVer", out SetupApiNative.InfContext context)) return null;
        string? date = ReadField(ref context, 1);
        string? version = ReadField(ref context, 2);
        return date is null || version is null ? date : date + "," + version;
    }

    private static bool MoveNext(ref SetupApiNative.InfContext context)
    {
        SetupApiNative.InfContext current = context;
        if (!SetupApiNative.SetupFindNextLine(ref current, out SetupApiNative.InfContext next)) return false;
        context = next; return true;
    }

    private static unsafe string? ReadField(ref SetupApiNative.InfContext context, uint field)
    {
        char[] buffer = new char[256];
        while (true)
        {
            fixed (char* pointer = buffer)
            {
                if (SetupApiNative.SetupGetStringFieldW(ref context, field, pointer, (uint)buffer.Length, out uint required))
                    return new string(pointer, 0, checked((int)required - 1));
                if (Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer) return null;
                buffer = new char[checked((int)required)];
            }
        }
    }

    private static unsafe string GetActualSection(nint inf, string section)
    {
        char[] buffer = new char[256];
        SetupApiNative.AlternatePlatformInfo platform = new()
        {
            Size = (uint)sizeof(SetupApiNative.AlternatePlatformInfo),
            Platform = PlatformWin32Nt,
            ProcessorArchitecture = ProcessorArchitectureAmd64
        };
        while (true)
        {
            fixed (char* pointer = buffer)
            {
                if (SetupApiNative.SetupDiGetActualSectionToInstallExW(inf, section, (nint)(&platform), pointer, (uint)buffer.Length, out uint required, out _, 0))
                    return new string(pointer, 0, checked((int)required - 1));
                int error = Marshal.GetLastPInvokeError();
                if (error != ErrorInsufficientBuffer) throw new Win32Exception(error, $"SetupDiGetActualSectionToInstallExW failed for section '{section}' with error {error}");
                buffer = new char[checked((int)required)];
            }
        }
    }
}
