using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using charmera_importer.Localization;
using charmera_importer.Models;
using charmera_importer.Services;
using charmera_importer.ViewModels;
using charmera_importer.Views;

[assembly: AvaloniaTestApplication(typeof(charmera_importer.Tests.TestApp))]

namespace charmera_importer.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<charmera_importer.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

// Drives the real main window with real (synthetic) mouse events, headless.
public class CameraPickerUiTests
{
    private static (MainViewModel Vm, MainWindow Window, string SettingsPath) Open(string? savedCameraId = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"charmera-ui-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(dir, "settings.json");
        var history = new JsonImportHistoryService(Path.Combine(dir, "history.json"));
        var settings = new JsonAppSettingsService(settingsPath);
        var initial = new AppSettings("en", null, null, null, CheckForUpdates: false, CameraVariantId: savedCameraId);

        LocalizedStrings.Instance.Apply("en");
        AppTheme.Apply(CameraVariant.FromId(savedCameraId)?.Palette ?? ThemePalette.Classic, animate: false);

        var vm = new MainViewModel(
            new LinuxRemovableDeviceService(), new PhotoScannerService(), new ExifService(), new ThumbnailService(),
            new ImportService(new Sha256HashingService(), history), history, settings, new GitHubUpdateService(),
            initial, LanguageOption.English, () => { });
        AppTheme.Follow(vm);

        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (vm, window, settingsPath);
    }

    private static Color Brush(string key) => ((ISolidColorBrush)Application.Current!.Resources[key]!).Color;

    private static void Click(Window window, Visual target)
    {
        var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseMove(center);
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    // The theme eases to its new colors over a fraction of a second; wait for that, up to a limit.
    // Awaiting (rather than sleeping) hands the thread back to the dispatcher, whose timers drive it.
    private static async Task<bool> WaitFor(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < until)
        {
            await Task.Delay(20);
        }

        return condition();
    }

    [AvaloniaFact]
    public async Task Clicking_a_camera_selects_it_recolors_the_app_and_is_remembered()
    {
        var (vm, window, settingsPath) = Open();
        var picker = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("cameraPicker"));
        var items = picker.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.Equal(7, items.Count);
        Assert.Null(vm.SelectedCameraVariant);
        Assert.Equal(ThemePalette.Classic.Header, Brush("HeaderBrush"));

        Click(window, items[CameraVariant.All.ToList().IndexOf(CameraVariant.Blue)]);
        Assert.Same(CameraVariant.Blue, vm.SelectedCameraVariant);
        Assert.True(await WaitFor(() => Brush("AccentBrush") == CameraVariant.Blue.Palette.Accent), $"theme never reached blue (accent is {Brush("AccentBrush")})");

        Assert.Same(CameraVariant.Blue, vm.SelectedCameraVariant);
        Assert.Equal(CameraVariant.Blue.Palette.Accent, Brush("AccentBrush"));
        Assert.Equal(CameraVariant.Blue.Palette.Header, Brush("HeaderBrush"));
        Assert.Equal(CameraVariant.Blue.Palette.OnAccent, Brush("OnAccentBrush"));

        // Saved for the next launch (the write is fire-and-forget, so give it a moment).
        for (var i = 0; i < 50 && !File.Exists(settingsPath); i++)
        {
            await Task.Delay(50);
        }

        Assert.Equal("blue", (await new JsonAppSettingsService(settingsPath).LoadAsync()).CameraVariantId);
    }

    [AvaloniaFact]
    public async Task Switching_between_cameras_ends_on_the_last_ones_colors()
    {
        var (vm, window, _) = Open();
        var picker = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("cameraPicker"));
        var items = picker.GetVisualDescendants().OfType<ListBoxItem>().ToList();

        Click(window, items[0]);
        Click(window, items[6]);
        Assert.True(await WaitFor(() => Brush("AccentBrush") == CameraVariant.Transparent.Palette.Accent), "theme never settled");

        Assert.Same(CameraVariant.Transparent, vm.SelectedCameraVariant);
        Assert.Equal(CameraVariant.Transparent.Palette.Accent, Brush("AccentBrush"));
    }

    [AvaloniaFact]
    public void A_saved_camera_is_selected_and_themed_from_the_first_frame()
    {
        var (vm, window, _) = Open(savedCameraId: "yellow");
        var picker = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("cameraPicker"));

        Assert.Same(CameraVariant.Yellow, vm.SelectedCameraVariant);
        Assert.Same(CameraVariant.Yellow, picker.SelectedItem);
        Assert.Equal(CameraVariant.Yellow.Palette.Header, Brush("HeaderBrush"));
        // Dark text on the yellow accent, not white.
        Assert.Equal(ThemePalette.Ink, Brush("OnAccentBrush"));
    }
}
