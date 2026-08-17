using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using DrvCtl.Drivers;

namespace DrvCtl.Research.Task8;

internal static class DriverVersionValuePredictor
{
    private static ReadOnlySpan<byte> ObservedHeader => [0x00, 0xFF, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00];

    internal static byte[] PredictCoreValue(string classGuid, string driverVersion)
    {
        string[] parts = driverVersion.Split(',', 2);
        if (parts.Length != 2) throw new FormatException($"DriverVer must contain date and version: {driverVersion}");
        DateOnly date = DateOnly.ParseExact(
            parts[0].Trim(),
            ["M/d/yyyy", "MM/dd/yyyy", "M/dd/yyyy", "MM/d/yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None);
        ushort[] components = parts[1].Split('.').Select(component => ushort.Parse(component, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray();
        if (components.Length != 4) throw new FormatException($"DriverVer must have four version components: {driverVersion}");

        byte[] result = new byte[40];
        ObservedHeader.CopyTo(result);
        new Guid(classGuid).TryWriteBytes(result.AsSpan(8, 16));
        long fileTime = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(24, 8), fileTime);
        for (int index = 0; index < components.Length; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(32 + (index * 2), 2), components[components.Length - 1 - index]);
        return result;
    }
}

internal sealed record DescriptorPrediction(string Id, string Configuration, string Description, string Manufacturer, string[] RequiredStrings);

internal static class DescriptorPredictor
{
    internal static DescriptorPrediction[] Predict(InfInspection inspection)
    {
        List<DescriptorPrediction> predictions = [];
        foreach (InfModelEntry model in inspection.Models)
        {
            string descriptionToken = FindUniqueToken(inspection.Strings, model.Description);
            string manufacturerToken = FindUniqueToken(inspection.Strings, model.Manufacturer);
            foreach (string id in model.Ids)
                predictions.Add(new(id, model.InstallSection, $"%{descriptionToken.ToLowerInvariant()}%", $"%{manufacturerToken.ToLowerInvariant()}%", [descriptionToken.ToLowerInvariant(), manufacturerToken.ToLowerInvariant()]));
        }
        return [.. predictions];
    }

    private static string FindUniqueToken(InfStringValue[] strings, string expanded)
    {
        string[] matches = strings.Where(value => value.Value.Equals(expanded, StringComparison.OrdinalIgnoreCase)).Select(value => value.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return matches.Length > 0 ? matches[0] : expanded;
    }
}

internal sealed record ServiceMetadataPrediction(string ServiceName, string DisplayName, string[] Owners);

internal static class ServiceMetadataPredictor
{
    internal static ServiceMetadataPrediction[] Predict(InfInspection inspection, string publishedInf)
    {
        List<ServiceMetadataPrediction> results = [];
        foreach (InfServiceOperation service in inspection.ServiceOperations.Where(service => !string.IsNullOrWhiteSpace(service.DisplayName)))
        {
            string[] tokens = inspection.Strings.Where(value => value.Value.Equals(service.DisplayName, StringComparison.Ordinal)).Select(value => value.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (tokens.Length != 1) continue;
            results.Add(new(service.Name, $"@{publishedInf},%{tokens[0]}%;{service.DisplayName}", [publishedInf]));
        }
        return [.. results];
    }
}

internal sealed record PnpLockdownPrediction(string DestinationKey, string Source, string[] Owners, int Class);

internal static class PnpLockdownPredictor
{
    internal static PnpLockdownPrediction Predict(string destination, string repositoryIdentity, string sourceFile, string publishedInf, int? pnpLockdown)
    {
        string destinationKey = "%SystemRoot%/" + destination.Replace('\\', '/').Replace("Windows/", string.Empty, StringComparison.OrdinalIgnoreCase);
        string source = $@"%SystemRoot%\System32\DriverStore\FileRepository\{repositoryIdentity}\{sourceFile}";
        int ownershipClass = pnpLockdown == 1 ? 4 : 5;
        return new(destinationKey, source, [publishedInf], ownershipClass);
    }
}
