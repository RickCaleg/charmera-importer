using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using charmera_importer.Localization;
using charmera_importer.Services;
using charmera_importer.ViewModels;
using charmera_importer.Views;

namespace charmera_importer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            IRemovableDeviceService deviceService = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new WindowsRemovableDeviceService()
                : new LinuxRemovableDeviceService();

            var appDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "charmera-importer");
            var historyFilePath = Path.Combine(appDataDirectory, "import-history.json");
            var settingsFilePath = Path.Combine(appDataDirectory, "settings.json");

            var hashingService = new Sha256HashingService();
            var historyService = new JsonImportHistoryService(historyFilePath);
            var settingsService = new JsonAppSettingsService(settingsFilePath);

            // A tiny local read, done synchronously here so the very first frame already
            // renders with the saved language and preferences already applied. It runs on the
            // thread pool: blocking the UI thread on LoadAsync directly deadlocks as soon as a
            // settings file exists, because its awaits try to resume on this same UI thread.
            var initialSettings = Task.Run(() => settingsService.LoadAsync()).GetAwaiter().GetResult();
            var initialLanguage = initialSettings.LanguageCode is not null
                ? LanguageOption.FromCode(initialSettings.LanguageCode)
                : LanguageOption.DetectSystem();
            LocalizedStrings.Instance.Apply(initialLanguage.Code);

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(
                    deviceService,
                    new PhotoScannerService(),
                    new ExifService(),
                    new ThumbnailService(),
                    new ImportService(hashingService, historyService),
                    historyService,
                    settingsService,
                    new GitHubUpdateService(),
                    initialSettings,
                    initialLanguage,
                    () => desktop.Shutdown()),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}