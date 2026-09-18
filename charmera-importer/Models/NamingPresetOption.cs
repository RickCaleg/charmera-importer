using System;
using System.Globalization;

namespace charmera_importer.Models;

public sealed record NamingPresetOption(FileNamingPreset Value, string DisplayName, string DateFormat)
{
    public string Format(DateTime date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static readonly NamingPresetOption[] All =
    [
        new(FileNamingPreset.CompactDateTime, "20260315_143022", "yyyyMMdd_HHmmss"),
        new(FileNamingPreset.DashedDateTime, "2026-03-15_14-30-22", "yyyy-MM-dd_HH-mm-ss"),
    ];
}
