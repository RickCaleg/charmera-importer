using System;
using System.Globalization;
using Avalonia.Data.Converters;
using charmera_importer.Localization;
using charmera_importer.Models;

namespace charmera_importer.Converters;

// A camera's display name in the current language, for tooltips and screen readers.
public sealed class CameraNameConverter : IValueConverter
{
    public static readonly CameraNameConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CameraVariant variant ? LocalizedStrings.Instance[variant.NameKey] : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
