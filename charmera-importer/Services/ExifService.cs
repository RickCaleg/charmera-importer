using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using charmera_importer.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;

namespace charmera_importer.Services;

public sealed class ExifService : IExifService
{
    public Task<PhotoExifData?> ReadAsync(string filePath, CancellationToken ct = default)
    {
        return Task.Run<PhotoExifData?>(() =>
        {
            ct.ThrowIfCancellationRequested();

            if (CharmeraAvi.IsAviName(filePath))
            {
                return ReadVideo(filePath);
            }

            // Charmera files get our own tolerant reading: their EXIF is structurally broken, so
            // generic readers miss the date and report the wrong dimensions (see CharmeraExif).
            var charmera = CharmeraExif.IsCharmeraFile(filePath) ? TryReadCharmera(filePath) : null;

            IReadOnlyList<MetadataExtractor.Directory> directories;
            try
            {
                directories = ImageMetadataReader.ReadMetadata(filePath);
            }
            catch
            {
                // Not every file yields readable metadata (unsupported format, corrupt file, etc.) —
                // treat this as "no EXIF available" rather than failing the whole scan.
                directories = [];
                if (charmera is null)
                {
                    return null;
                }
            }

            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var gps = directories.OfType<GpsDirectory>().FirstOrDefault();

            string? cameraMake = ifd0?.GetString(ExifDirectoryBase.TagMake)?.Trim();
            string? cameraModel = ifd0?.GetString(ExifDirectoryBase.TagModel)?.Trim();

            var dateTaken = TryGetDate(subIfd, ExifDirectoryBase.TagDateTimeOriginal)
                ?? TryGetDate(subIfd, ExifDirectoryBase.TagDateTimeDigitized)
                ?? TryGetDate(ifd0, ExifDirectoryBase.TagDateTime);

            // The JPEG frame header is the ground truth for pixel size; EXIF dimension tags can
            // be wrong (the Charmera claims 640x480 for 1440x1080 images), so they're a fallback.
            var jpeg = directories.OfType<JpegDirectory>().FirstOrDefault();
            int? width = TryGetInt(jpeg, JpegDirectory.TagImageWidth)
                ?? TryGetInt(subIfd, ExifDirectoryBase.TagExifImageWidth)
                ?? TryGetInt(ifd0, ExifDirectoryBase.TagImageWidth);
            int? height = TryGetInt(jpeg, JpegDirectory.TagImageHeight)
                ?? TryGetInt(subIfd, ExifDirectoryBase.TagExifImageHeight)
                ?? TryGetInt(ifd0, ExifDirectoryBase.TagImageHeight);

            if (charmera is not null)
            {
                // The file says "Generalplus"/"CBB3" (the chip); the import writes the camera.
                cameraMake = CharmeraExif.CameraMake;
                cameraModel = CharmeraExif.CameraModel;
                dateTaken = charmera.DateTaken ?? dateTaken;
                width = charmera.Width > 0 ? charmera.Width : width;
                height = charmera.Height > 0 ? charmera.Height : height;
            }

            string? orientation = ifd0 is not null && ifd0.ContainsTag(ExifDirectoryBase.TagOrientation)
                ? ifd0.GetDescription(ExifDirectoryBase.TagOrientation)
                : null;

            double? focalLength = TryGetDouble(subIfd, ExifDirectoryBase.TagFocalLength);
            double? exposureTime = TryGetDouble(subIfd, ExifDirectoryBase.TagExposureTime);
            double? fNumber = TryGetDouble(subIfd, ExifDirectoryBase.TagFNumber);
            int? isoSpeed = TryGetInt(subIfd, ExifDirectoryBase.TagIsoEquivalent);

            string? gpsLatLong = null;
            if (gps is not null && gps.TryGetGeoLocation(out var location) && !location.IsZero)
            {
                gpsLatLong = $"{location.Latitude}, {location.Longitude}";
            }

            var allTags = new Dictionary<string, string>();
            foreach (var directory in directories)
            {
                foreach (var tag in directory.Tags)
                {
                    var key = $"{directory.Name} - {tag.Name}";
                    allTags[key] = tag.Description ?? string.Empty;
                }
            }

            return new PhotoExifData(
                cameraMake,
                cameraModel,
                dateTaken,
                width,
                height,
                orientation,
                focalLength,
                exposureTime,
                fNumber,
                isoSpeed,
                gpsLatLong,
                allTags,
                charmera is not null);
        }, ct);
    }

    // Videos have no EXIF; their facts come from the AVI headers. The recording date is the
    // file's timestamp (written by the camera), because the one inside the file is the
    // Charmera's hard-coded 2010-06-29 — unless the file holds a genuine date.
    private static PhotoExifData? ReadVideo(string filePath)
    {
        CharmeraAviInfo info;
        try
        {
            info = CharmeraAvi.Read(filePath);
        }
        catch
        {
            return null;
        }

        var embedded = info.EmbeddedDate;
        var date = embedded is not null && !CharmeraAvi.HasBogusDate(info)
            ? embedded
            : File.GetLastWriteTime(filePath);

        var tags = new Dictionary<string, string>();
        if (info.Duration is { } duration)
        {
            tags["AVI - Duration"] = duration.ToString(@"m\:ss\.f", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (info.FramesPerSecond is { } fps)
        {
            tags["AVI - Frame rate"] = $"{fps:0.##} fps";
        }

        if (info.VideoCodec is { Length: > 0 } codec)
        {
            tags["AVI - Video codec"] = codec;
        }

        if (info.AudioSampleRate is { } rate)
        {
            tags["AVI - Audio"] = $"PCM {rate} Hz, {info.AudioBitsPerSample} bit, {info.AudioChannels} ch";
        }

        foreach (var field in info.DateFields)
        {
            tags[$"AVI - {field.ChunkId} (embedded date)"] = field.Value.Trim('\0', ' ', '\n');
        }

        return new PhotoExifData(
            CharmeraExif.CameraMake,
            CharmeraExif.CameraModel,
            date,
            info.Width > 0 ? info.Width : null,
            info.Height > 0 ? info.Height : null,
            null, null, null, null, null, null,
            tags,
            IsCharmera: true,
            Duration: info.Duration);
    }

    private static CharmeraExifInfo? TryReadCharmera(string filePath)
    {
        try
        {
            return CharmeraExif.Read(File.ReadAllBytes(filePath));
        }
        catch
        {
            return null;
        }
    }

    // Standard EXIF dates first; then the raw string, which also covers the Charmera's
    // "YYYY:MM:DD:HH:MM:SS" form that MetadataExtractor can't parse.
    private static DateTime? TryGetDate(MetadataExtractor.Directory? directory, int tagType)
    {
        if (directory is null || !directory.ContainsTag(tagType))
        {
            return null;
        }

        if (directory.TryGetDateTime(tagType, out var date))
        {
            return date;
        }

        return CharmeraExif.ParseExifDate(directory.GetString(tagType));
    }

    private static int? TryGetInt(MetadataExtractor.Directory? directory, int tagType)
    {
        if (directory is null || !directory.ContainsTag(tagType))
        {
            return null;
        }

        try
        {
            return directory.GetInt32(tagType);
        }
        catch
        {
            return null;
        }
    }

    private static double? TryGetDouble(MetadataExtractor.Directory? directory, int tagType)
    {
        if (directory is null || !directory.ContainsTag(tagType))
        {
            return null;
        }

        try
        {
            return directory.GetDouble(tagType);
        }
        catch
        {
            return null;
        }
    }
}
