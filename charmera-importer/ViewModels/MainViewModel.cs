using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using charmera_importer.Localization;
using charmera_importer.Models;
using charmera_importer.Services;

namespace charmera_importer.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const int ThumbnailAndExifConcurrency = 4;

    // How often the device list is re-read, so plugging the camera in "just works" without
    // pressing refresh. Reading /proc/mounts (or DriveInfo) is cheap enough for this.
    private static readonly TimeSpan DevicePollInterval = TimeSpan.FromSeconds(3);

    // Stand-in photo for the destination path preview, so it reads the same with or without a
    // camera connected (real photos' EXIF arrives asynchronously and would make it jump).
    private static readonly PhotoImportCandidate PreviewSamplePhoto = new()
    {
        SourcePath = "IMG_0001.JPG",
        FileName = "IMG_0001.JPG",
        FileSizeBytes = 0,
        Exif = new PhotoExifData(null, "Kodak PIXPRO", new DateTime(2026, 3, 15, 14, 30, 22),
            null, null, null, null, null, null, null, null, new Dictionary<string, string>()),
    };

    private readonly IRemovableDeviceService deviceService;
    private readonly IPhotoScannerService scannerService;
    private readonly IExifService exifService;
    private readonly IThumbnailService thumbnailService;
    private readonly IImportService importService;
    private readonly IImportHistoryService historyService;
    private readonly IAppSettingsService settingsService;
    private readonly IUpdateService updateService;
    private readonly Action requestShutdown;

    private CancellationTokenSource? scanCts;
    private readonly DispatcherTimer devicePollTimer;
    private bool isRefreshingDevices;
    private bool suppressSettingsPersistence;

    public IReadOnlyList<FolderOrganizationOption> OrganizationOptions { get; } = FolderOrganizationOption.All;
    public IReadOnlyList<NamingPresetOption> NamingPresets { get; } = NamingPresetOption.All;
    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } = LanguageOption.All;

    [ObservableProperty]
    public partial ObservableCollection<RemovableDevice> Devices { get; set; } = new();

    [ObservableProperty]
    public partial RemovableDevice? SelectedDevice { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<PhotoItemViewModel> Photos { get; set; } = new();

    [ObservableProperty]
    public partial PhotoItemViewModel? SelectedPhoto { get; set; }

    [ObservableProperty]
    public partial string? DestinationRootPath { get; set; }

    [ObservableProperty]
    public partial FolderOrganizationOption SelectedOrganizationOption { get; set; }

    [ObservableProperty]
    public partial NamingPresetOption SelectedNamingPreset { get; set; }

    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool AppendOriginalFileName { get; set; } = true;

    // Deliberately not persisted — see AppSettings' remarks. Always starts unchecked.
    [ObservableProperty]
    public partial bool DeleteSourceAfterImport { get; set; }

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    public partial int ImportProgressCurrent { get; set; }

    [ObservableProperty]
    public partial int ImportProgressTotal { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool CheckForUpdatesAutomatically { get; set; } = true;


    [ObservableProperty]
    public partial UpdateInfo? AvailableUpdate { get; set; }

    [ObservableProperty]
    public partial bool IsUpdateDismissed { get; set; }

    [ObservableProperty]
    public partial bool IsCheckingForUpdates { get; set; }

    [ObservableProperty]
    public partial bool IsInstallingUpdate { get; set; }

    [ObservableProperty]
    public partial double UpdateDownloadProgress { get; set; }

    [ObservableProperty]
    public partial string? UpdateStatusMessage { get; set; }

    public bool ShowUpdateBanner => AvailableUpdate is not null && !IsUpdateDismissed;
    public bool IsManualUpdate => AvailableUpdate?.InstallMode == UpdateInstallMode.Manual;
    public string UpdateBannerText => AvailableUpdate is null
        ? string.Empty
        : LocalizedStrings.Instance.UpdateAvailable(AvailableUpdate.Version.ToString(3), updateService.CurrentVersion.ToString(3));
    public string UpdateActionLabel => IsManualUpdate
        ? LocalizedStrings.Instance.UpdateDownloadButton
        : LocalizedStrings.Instance.UpdateInstallButton;
    public string CurrentVersionLabel => LocalizedStrings.Instance.VersionLabel(updateService.CurrentVersion.ToString(3));
    public string VersionOnlyLabel => LocalizedStrings.Instance.VersionOnly(updateService.CurrentVersion.ToString(3));

    public bool HasDevice => SelectedDevice is not null;
    public bool HasDestination => !string.IsNullOrWhiteSpace(DestinationRootPath);

    // Step completion drives the numbered badges in the workflow panel.
    public bool IsSourceStepDone => HasDevice && Photos.Count > 0 && !IsScanning;
    public bool IsDestinationStepDone => HasDestination;

    public bool ShowNoDeviceState => !HasDevice;
    public bool ShowScanningState => HasDevice && IsScanning && Photos.Count == 0;
    public bool ShowNoPhotosState => HasDevice && !IsScanning && Photos.Count == 0;

    private int VideoCount => Photos.Count(p => p.Candidate.IsVideo);
    private int PhotoOnlyCount => Photos.Count - VideoCount;

    public string PhotosCountLabel => LocalizedStrings.Instance.MediaSummary(PhotoOnlyCount, VideoCount);
    public string DestinationRootPathDisplay => DestinationRootPath ?? LocalizedStrings.Instance.NoDestinationSelected;

    public string DestinationFolderName => string.IsNullOrWhiteSpace(DestinationRootPath)
        ? string.Empty
        : Path.GetFileName(Path.TrimEndingDirectorySeparator(DestinationRootPath)) is { Length: > 0 } name
            ? name
            : DestinationRootPath;

    public string SourceSummary => !HasDevice ? LocalizedStrings.Instance.SourceNoDevice
        : IsScanning && Photos.Count == 0 ? LocalizedStrings.Instance.ScanningStatus
        : LocalizedStrings.Instance.MediaSummary(PhotoOnlyCount, VideoCount);

    public string DestinationPreview
    {
        get
        {
            var settings = BuildImportSettings(string.Empty);
            var folder = ImportPathResolver.ResolveDestinationFolder(PreviewSamplePhoto, settings);
            var fileName = ImportPathResolver.ResolveDestinationFileName(PreviewSamplePhoto, settings);
            // Zero-width spaces after each separator let the path wrap between folders instead
            // of mid-name when the panel is narrow.
            return Path.Combine(folder, fileName)
                .Replace(Path.DirectorySeparatorChar.ToString(), Path.DirectorySeparatorChar + "\u200B");
        }
    }

    public string ImportButtonLabel => LocalizedStrings.Instance.ImportButton(PhotoOnlyCount, VideoCount);

    // Explains a disabled Import button instead of leaving the user guessing.
    public string? ImportHint => IsImporting ? null
        : !HasDevice ? LocalizedStrings.Instance.HintSelectCamera
        : !IsScanning && Photos.Count == 0 ? LocalizedStrings.Instance.HintNoPhotos
        : !HasDestination ? LocalizedStrings.Instance.HintChooseDestination
        : null;

    public bool ShowImportHint => ImportHint is not null;

    public MainViewModel(
        IRemovableDeviceService deviceService,
        IPhotoScannerService scannerService,
        IExifService exifService,
        IThumbnailService thumbnailService,
        IImportService importService,
        IImportHistoryService historyService,
        IAppSettingsService settingsService,
        IUpdateService updateService,
        AppSettings initialSettings,
        LanguageOption initialLanguage,
        Action requestShutdown)
    {
        this.deviceService = deviceService;
        this.scannerService = scannerService;
        this.exifService = exifService;
        this.thumbnailService = thumbnailService;
        this.importService = importService;
        this.historyService = historyService;
        this.settingsService = settingsService;
        this.updateService = updateService;
        this.requestShutdown = requestShutdown;

        // LocalizedStrings was already applied to initialLanguage by the caller (App.axaml.cs) —
        // restoring saved preferences here just reflects that, without re-persisting them right
        // back (which would be redundant, and would clobber a not-yet-loaded settings file if
        // this ran before LoadAsync's result came back).
        suppressSettingsPersistence = true;
        SelectedLanguage = initialLanguage;
        SelectedOrganizationOption = OrganizationOptions.FirstOrDefault(o => o.Value == initialSettings.OrganizationScheme)
            ?? OrganizationOptions.First();
        SelectedNamingPreset = NamingPresets.FirstOrDefault(p => p.Value == initialSettings.NamingPreset)
            ?? NamingPresets.First();
        DestinationRootPath = initialSettings.DestinationRootPath;
        AppendOriginalFileName = initialSettings.AppendOriginalFileName;
        CheckForUpdatesAutomatically = initialSettings.CheckForUpdates;
        suppressSettingsPersistence = false;

        LocalizedStrings.Instance.PropertyChanged += OnLocalizationChanged;

        _ = historyService.LoadAsync();
        _ = RefreshDevicesCommand.ExecuteAsync(null);
        devicePollTimer = new DispatcherTimer { Interval = DevicePollInterval };
        devicePollTimer.Tick += async (_, _) => await RefreshDevicesAsync();
        devicePollTimer.Start();
        if (CheckForUpdatesAutomatically)
        {
            _ = CheckForUpdatesInBackgroundAsync();
        }
    }

    // Parameterless constructor kept only for the Avalonia XAML previewer's Design.DataContext.
    public MainViewModel() : this(
        new LinuxRemovableDeviceService(),
        new PhotoScannerService(),
        new ExifService(),
        new ThumbnailService(),
        new ImportService(new Sha256HashingService(), new JsonImportHistoryService(string.Empty)),
        new JsonImportHistoryService(string.Empty),
        new JsonAppSettingsService(string.Empty),
        new GitHubUpdateService(),
        new AppSettings(null, null, null, null, CheckForUpdates: false),
        LanguageOption.English,
        () => { })
    {
    }

    private void RaiseWorkflowStateChanged()
    {
        OnPropertyChanged(nameof(HasDevice));
        OnPropertyChanged(nameof(HasDestination));
        OnPropertyChanged(nameof(IsSourceStepDone));
        OnPropertyChanged(nameof(IsDestinationStepDone));
        OnPropertyChanged(nameof(ShowNoDeviceState));
        OnPropertyChanged(nameof(ShowScanningState));
        OnPropertyChanged(nameof(ShowNoPhotosState));
        OnPropertyChanged(nameof(PhotosCountLabel));
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(DestinationPreview));
        OnPropertyChanged(nameof(ImportButtonLabel));
        OnPropertyChanged(nameof(ImportHint));
        OnPropertyChanged(nameof(ShowImportHint));
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RaiseWorkflowStateChanged();
        OnPropertyChanged(nameof(PhotosCountLabel));
        OnPropertyChanged(nameof(DestinationRootPathDisplay));
        OnPropertyChanged(nameof(UpdateBannerText));
        OnPropertyChanged(nameof(UpdateActionLabel));
        OnPropertyChanged(nameof(CurrentVersionLabel));
        OnPropertyChanged(nameof(VersionOnlyLabel));
        foreach (var photo in Photos)
        {
            photo.RefreshLocalizedText();
        }
    }

    private void PersistSettings()
    {
        if (suppressSettingsPersistence)
        {
            return;
        }

        var settings = new AppSettings(
            SelectedLanguage.Code,
            DestinationRootPath,
            SelectedOrganizationOption.Value,
            SelectedNamingPreset.Value,
            AppendOriginalFileName,
            CheckForUpdatesAutomatically);

        _ = settingsService.SaveAsync(settings);
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        LocalizedStrings.Instance.Apply(value.Code);
        PersistSettings();
    }

    partial void OnSelectedOrganizationOptionChanged(FolderOrganizationOption value)
    {
        OnPropertyChanged(nameof(DestinationPreview));
        PersistSettings();
    }

    partial void OnSelectedNamingPresetChanged(NamingPresetOption value)
    {
        OnPropertyChanged(nameof(DestinationPreview));
        PersistSettings();
    }

    partial void OnAppendOriginalFileNameChanged(bool value)
    {
        OnPropertyChanged(nameof(DestinationPreview));
        PersistSettings();
    }

    partial void OnCheckForUpdatesAutomaticallyChanged(bool value) => PersistSettings();

    partial void OnAvailableUpdateChanged(UpdateInfo? value)
    {
        OnPropertyChanged(nameof(ShowUpdateBanner));
        OnPropertyChanged(nameof(IsManualUpdate));
        OnPropertyChanged(nameof(UpdateBannerText));
        OnPropertyChanged(nameof(UpdateActionLabel));
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsUpdateDismissedChanged(bool value) => OnPropertyChanged(nameof(ShowUpdateBanner));

    partial void OnIsInstallingUpdateChanged(bool value)
    {
        InstallUpdateCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCheckingForUpdatesChanged(bool value) => CheckForUpdatesCommand.NotifyCanExecuteChanged();

    // Startup check: failures (offline, rate-limited) are silent — only a found update shows up.
    private async Task CheckForUpdatesInBackgroundAsync()
    {
        try
        {
            AvailableUpdate = await updateService.CheckForUpdateAsync();
        }
        catch
        {
            // No network is a normal state for this app — nothing to report.
        }
    }

    private bool CanCheckForUpdates() => !IsCheckingForUpdates;

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        UpdateStatusMessage = LocalizedStrings.Instance.UpdateChecking;
        try
        {
            AvailableUpdate = await updateService.CheckForUpdateAsync();
            IsUpdateDismissed = false;
            UpdateStatusMessage = AvailableUpdate is null ? LocalizedStrings.Instance.UpdateUpToDate : null;
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = LocalizedStrings.Instance.UpdateCheckFailed(ex.Message);
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    // Never while importing: replacing the app mid-copy would cut the import short.
    private bool CanInstallUpdate() => AvailableUpdate is not null && !IsInstallingUpdate && !IsImporting;

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (AvailableUpdate is not { } update)
        {
            return;
        }

        if (update.InstallMode == UpdateInstallMode.Manual)
        {
            updateService.OpenReleasePage(update);
            return;
        }

        IsInstallingUpdate = true;
        UpdateDownloadProgress = 0;
        UpdateStatusMessage = LocalizedStrings.Instance.UpdateDownloading(0);
        var progress = new Progress<double>(fraction =>
        {
            UpdateDownloadProgress = fraction;
            UpdateStatusMessage = LocalizedStrings.Instance.UpdateDownloading(fraction);
        });

        try
        {
            await updateService.InstallUpdateAsync(update, progress);
            UpdateStatusMessage = LocalizedStrings.Instance.UpdateInstalling;
            requestShutdown();
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = LocalizedStrings.Instance.UpdateError(ex.Message);
            IsInstallingUpdate = false;
        }
    }

    [RelayCommand]
    private void OpenReleaseNotes()
    {
        if (AvailableUpdate is { } update)
        {
            updateService.OpenReleasePage(update);
        }
    }

    [RelayCommand]
    private void DismissUpdate() => IsUpdateDismissed = true;

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        // The poll timer and the refresh button can overlap; one read at a time is plenty.
        if (isRefreshingDevices)
        {
            return;
        }

        isRefreshingDevices = true;
        try
        {
            var devices = await deviceService.GetRemovableDevicesAsync();
            SyncDevices(devices);
        }
        catch
        {
            // A transient read failure (e.g. a mount disappearing mid-read) just waits for the next poll.
        }
        finally
        {
            isRefreshingDevices = false;
        }
    }

    // Updates the list in place, matched by mount point, so a refresh never drops the current
    // selection (which would clear and rescan the photo grid). Only an unplugged device is
    // deselected; with exactly one camera connected and nothing selected, it's picked for you.
    private void SyncDevices(IReadOnlyList<RemovableDevice> current)
    {
        var currentPaths = current.Select(d => d.RootPath).ToHashSet();
        foreach (var gone in Devices.Where(d => !currentPaths.Contains(d.RootPath)).ToList())
        {
            Devices.Remove(gone);
        }

        var knownPaths = Devices.Select(d => d.RootPath).ToHashSet();
        foreach (var added in current.Where(d => !knownPaths.Contains(d.RootPath)))
        {
            Devices.Add(added);
        }

        if (SelectedDevice is not null && !currentPaths.Contains(SelectedDevice.RootPath))
        {
            SelectedDevice = null;
        }

        if (SelectedDevice is null && Devices.Count == 1)
        {
            SelectedDevice = Devices[0];
        }
    }

    partial void OnSelectedDeviceChanged(RemovableDevice? value)
    {
        RaiseWorkflowStateChanged();
        _ = ScanSelectedDeviceCommand.ExecuteAsync(null);
    }

    partial void OnPhotosChanged(ObservableCollection<PhotoItemViewModel> oldValue, ObservableCollection<PhotoItemViewModel> newValue)
    {
        oldValue.CollectionChanged -= OnPhotosCollectionChanged;
        newValue.CollectionChanged += OnPhotosCollectionChanged;
        ImportCommand.NotifyCanExecuteChanged();
        RaiseWorkflowStateChanged();
    }

    private void OnPhotosCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        ImportCommand.NotifyCanExecuteChanged();
        RaiseWorkflowStateChanged();
    }

    partial void OnDestinationRootPathChanged(string? value)
    {
        ImportCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DestinationRootPathDisplay));
        OnPropertyChanged(nameof(DestinationFolderName));
        RaiseWorkflowStateChanged();
        PersistSettings();
    }

    partial void OnIsImportingChanged(bool value)
    {
        OnPropertyChanged(nameof(ImportHint));
        OnPropertyChanged(nameof(ShowImportHint));
        ImportCommand.NotifyCanExecuteChanged();
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsScanningChanged(bool value) => RaiseWorkflowStateChanged();

    [RelayCommand]
    private async Task ScanSelectedDeviceAsync()
    {
        scanCts?.Cancel();
        Photos.Clear();
        SelectedPhoto = null;

        if (SelectedDevice is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        scanCts = cts;

        IsScanning = true;
        // Scan progress/results are shown by the Camera step itself (SourceSummary); the footer
        // status line is reserved for the import.
        StatusMessage = null;
        try
        {
            var candidates = await scannerService.ScanAsync(SelectedDevice.RootPath, cts.Token);
            var itemViewModels = candidates.Select(c => new PhotoItemViewModel(c)).ToList();
            Photos = new ObservableCollection<PhotoItemViewModel>(itemViewModels);

            await PopulateThumbnailsAndExifAsync(itemViewModels, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer scan (device changed) — nothing to do.
        }
        finally
        {
            if (scanCts == cts)
            {
                IsScanning = false;
            }
        }
    }

    private async Task PopulateThumbnailsAndExifAsync(IReadOnlyList<PhotoItemViewModel> items, CancellationToken ct)
    {
        using var throttle = new SemaphoreSlim(ThumbnailAndExifConcurrency);

        var tasks = items.Select(async item =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                var thumbnailTask = thumbnailService.CreateThumbnailAsync(item.Candidate.SourcePath, ct: ct);
                var exifTask = exifService.ReadAsync(item.Candidate.SourcePath, ct);
                await Task.WhenAll(thumbnailTask, exifTask);

                var thumbnail = thumbnailTask.Result;
                var exif = exifTask.Result;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    item.ApplyThumbnail(thumbnail);
                    item.ApplyExif(exif);
                });
            }
            catch (OperationCanceledException)
            {
                // Scan was superseded — ignore.
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private ImportSettings BuildImportSettings(string destinationRootPath) => new()
    {
        DestinationRootPath = destinationRootPath,
        OrganizationScheme = SelectedOrganizationOption.Value,
        NamingPreset = SelectedNamingPreset.Value,
        AppendOriginalFileName = AppendOriginalFileName,
        DeleteSourceAfterImport = DeleteSourceAfterImport,
    };

    [RelayCommand]
    private void CloseDetails() => SelectedPhoto = null;

    private bool CanImport() =>
        Photos.Count > 0 && !string.IsNullOrWhiteSpace(DestinationRootPath) && !IsImporting && !IsInstallingUpdate;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        if (DestinationRootPath is null)
        {
            return;
        }

        var settings = BuildImportSettings(DestinationRootPath);

        IsImporting = true;
        ImportProgressCurrent = 0;
        ImportProgressTotal = Photos.Count;

        var progress = new Progress<ImportProgress>(p =>
        {
            ImportProgressCurrent = p.Completed;
            ImportProgressTotal = p.Total;
            StatusMessage = LocalizedStrings.Instance.Importing(p.CurrentFileName, p.Completed, p.Total);
        });

        try
        {
            await importService.ImportAsync(Photos.Select(p => p.Candidate).ToList(), settings, progress);

            foreach (var photo in Photos)
            {
                photo.ApplyStatus(photo.Candidate.Status, photo.Candidate.StatusMessage);
            }

            StatusMessage = LocalizedStrings.Instance.ImportCompleteStatus;
        }
        finally
        {
            IsImporting = false;
        }
    }
}
