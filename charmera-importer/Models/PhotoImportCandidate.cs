using System;
using Avalonia.Media.Imaging;

namespace charmera_importer.Models;

public sealed class PhotoImportCandidate
{
    public required string SourcePath { get; init; }
    public required string FileName { get; init; }
    public required long FileSizeBytes { get; init; }
    public DateTime? FileSystemDateModified { get; init; }

    // A Charmera video (Motion-JPEG AVI) rather than a photo.
    public bool IsVideo { get; init; }
    public PhotoExifData? Exif { get; set; }
    public string? Sha256Hash { get; set; }
    public Bitmap? Thumbnail { get; set; }
    public ImportStatus Status { get; set; } = ImportStatus.Pending;
    public string? StatusMessage { get; set; }

    public DateTime ResolveDate() =>
        Exif?.DateTaken ?? FileSystemDateModified ?? DateTime.Now;
}
