/*
 * Runtime version display. The csproj is the single source of truth (see
 * <Version>/<InformationalVersion> in drvctl.csproj). This class just reads
 * what the SDK already baked into the binary.
 */

using System.Diagnostics;

namespace DrvCtl.Core;

/// Reads the running executable's own InformationalVersion (ProductVersion in
/// the Windows PE resource) rather than a hard-coded string. Native AOT/single-
/// file builds don't expose a usable Assembly.Location, so this reads the
/// process's own path instead - both cases are populated by the SDK from the
/// csproj's Version/InformationalVersion properties, no source-text substitution needed.
internal static class VersionInfo
{
    internal static string Current { get; } = Resolve();

    private static string Resolve()
    {
        try
        {
            string? path =
                Environment.ProcessPath;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return "unknown";
            }

            string? productVersion =
                FileVersionInfo.GetVersionInfo(path).ProductVersion;

            return string.IsNullOrWhiteSpace(productVersion)
                ? "unknown"
                : productVersion;
        }
        catch
        {
            return "unknown";
        }
    }
}
