namespace DrvCtl.Utilities;

/// Human-friendly value formatting shared across console output.
internal static class Formatters
{
    /// Formats a byte count using binary units (KiB/MiB/GiB), matching the
    /// units drvctl's own output uses everywhere else.
    internal static string Bytes(
        long bytes
    )
    {
        const double KiB = 1024.0;
        const double MiB = KiB * 1024.0;
        const double GiB = MiB * 1024.0;

        double value =
            bytes;

        if (value >= GiB)
        {
            return $"{value / GiB:F2} GiB";
        }

        if (value >= MiB)
        {
            return $"{value / MiB:F2} MiB";
        }

        if (value >= KiB)
        {
            return $"{value / KiB:F2} KiB";
        }

        return $"{bytes} bytes";
    }
}
