using System;
using System.Collections.Generic;

namespace charmera_importer.Models;

public sealed record PhotoExifData(
    string? CameraMake,
    string? CameraModel,
    DateTime? DateTaken,
    int? Width,
    int? Height,
    string? Orientation,
    double? FocalLengthMm,
    double? ExposureTimeSeconds,
    double? FNumber,
    int? IsoSpeed,
    string? GpsLatLong,
    IReadOnlyDictionary<string, string> AllTags,
    // Taken by a Kodak Charmera (detected by its encoder signature): the values above come
    // from the repaired reading, and the import rewrites the file's broken EXIF.
    bool IsCharmera = false,
    // Videos only.
    TimeSpan? Duration = null);
