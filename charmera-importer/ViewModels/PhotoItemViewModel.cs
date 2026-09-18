using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using charmera_importer.Localization;
using charmera_importer.Models;

namespace charmera_importer.ViewModels;

public partial class PhotoItemViewModel : ViewModelBase
{
    public PhotoImportCandidate Candidate { get; }

    [ObservableProperty]
    public partial Bitmap? Thumbnail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusBadge))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    public partial ImportStatus Status { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public string FileName => Candidate.FileName;
    public string? CameraMake => Candidate.Exif?.CameraMake;
    public string? CameraModel => Candidate.Exif?.CameraModel;
    public DateTime? DateTaken => Candidate.Exif?.DateTaken;

    // Formatted for the app's language, not the OS locale (they can differ).
    public string DateTakenDisplay => DateTaken?.ToString("g", LocalizedStrings.Instance.Culture) ?? string.Empty;
    public int? Width => Candidate.Exif?.Width;
    public int? Height => Candidate.Exif?.Height;
    public IEnumerable<string> AllTags =>
        Candidate.Exif?.AllTags.Select(kv => $"{kv.Key}: {kv.Value}") ?? Enumerable.Empty<string>();
    public bool HasStatusBadge => Status != ImportStatus.Pending;
    public string StatusLabel => LocalizedStrings.Instance.GetStatusLabel(Status);

    // Cameras vary widely in what EXIF they write (this Kodak writes none at all) — the detail
    // panel hides rows with no data instead of showing blank "Marca:" / "Modelo:" labels.
    public bool HasCameraMake => !string.IsNullOrWhiteSpace(CameraMake);
    public bool HasCameraModel => !string.IsNullOrWhiteSpace(CameraModel);
    public bool HasDateTaken => DateTaken.HasValue;
    public bool HasDimensions => Width.HasValue && Height.HasValue;
    public bool HasAnyBasicExifInfo => HasCameraMake || HasCameraModel || HasDateTaken || HasDimensions;
    public bool IsCharmera => Candidate.Exif?.IsCharmera == true;
    public bool IsVideo => Candidate.IsVideo;
    public bool IsPhoto => !Candidate.IsVideo;
    public bool HasDuration => Candidate.Exif?.Duration is not null;
    // Rounded, not truncated: frame timing makes a 2 s clip 1.99998 s long.
    public string DurationDisplay => Candidate.Exif?.Duration is { } exact
        && TimeSpan.FromSeconds(Math.Round(exact.TotalSeconds)) is var d
        ? d.ToString(d.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", System.Globalization.CultureInfo.InvariantCulture)
        : string.Empty;
    public string VideoBadge => HasDuration ? $"{LocalizedStrings.Instance.CardVideo} · {DurationDisplay}" : LocalizedStrings.Instance.CardVideo;
    public string DetailTitle => IsVideo ? LocalizedStrings.Instance.DetailVideoTitle : LocalizedStrings.Instance.DetailTitle;

    public PhotoItemViewModel(PhotoImportCandidate candidate)
    {
        Candidate = candidate;
        Thumbnail = candidate.Thumbnail;
        Status = candidate.Status;
        StatusMessage = candidate.StatusMessage;
    }

    public void ApplyExif(PhotoExifData? exif)
    {
        Candidate.Exif = exif;
        OnPropertyChanged(nameof(CameraMake));
        OnPropertyChanged(nameof(CameraModel));
        OnPropertyChanged(nameof(DateTaken));
        OnPropertyChanged(nameof(DateTakenDisplay));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(AllTags));
        OnPropertyChanged(nameof(HasCameraMake));
        OnPropertyChanged(nameof(HasCameraModel));
        OnPropertyChanged(nameof(HasDateTaken));
        OnPropertyChanged(nameof(HasDimensions));
        OnPropertyChanged(nameof(HasAnyBasicExifInfo));
        OnPropertyChanged(nameof(IsCharmera));
        OnPropertyChanged(nameof(HasDuration));
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(VideoBadge));
    }

    public void ApplyThumbnail(Bitmap? thumbnail)
    {
        Candidate.Thumbnail = thumbnail;
        Thumbnail = thumbnail;
    }

    public void ApplyStatus(ImportStatus status, string? statusMessage)
    {
        Candidate.Status = status;
        Candidate.StatusMessage = statusMessage;
        Status = status;
        StatusMessage = statusMessage;
    }

    // Called by MainViewModel (which owns the single LocalizedStrings subscription) for every
    // live photo item after a language switch, so already-rendered status badges retranslate.
    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(DateTakenDisplay));
        OnPropertyChanged(nameof(VideoBadge));
        OnPropertyChanged(nameof(DetailTitle));
    }
}
