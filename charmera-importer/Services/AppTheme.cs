using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using charmera_importer.Models;
using charmera_importer.ViewModels;

namespace charmera_importer.Services;

// Applies a ThemePalette to the running app. The themed brushes are single, mutable instances
// registered in Application.Resources and referenced with {DynamicResource}, so a change reaches
// every window at once and can be eased from the old color to the new one. The Fluent accent is
// updated alongside, which recolors the stock controls (check boxes, progress bars, focus rings).
public static class AppTheme
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    // Resource key -> where the color comes from in the palette.
    private static readonly (string Key, Func<ThemePalette, Color> Pick)[] Slots =
    [
        ("AccentBrush", p => p.Accent),
        ("AccentHoverBrush", p => p.AccentHover),
        ("AccentPressedBrush", p => p.AccentPressed),
        ("OnAccentBrush", p => p.OnAccent),
        ("AccentInkBrush", p => p.AccentInk),
        ("HeaderBrush", p => p.Header),
        ("OnHeaderBrush", p => p.OnHeader),
        ("OnHeaderMutedBrush", p => p.OnHeaderMuted),
        // Hover/pressed washes for controls on the header, toward the text color so they show on
        // dark and light headers alike.
        ("HeaderHoverBrush", p => ThemePalette.Mix(p.Header, p.OnHeader, 0.12)),
        ("HeaderPressedBrush", p => ThemePalette.Mix(p.Header, p.OnHeader, 0.22)),
        ("SurfaceAppBrush", p => p.Surface),
        ("TintBrush", p => p.Tint),
        ("TintBorderBrush", p => p.TintBorder),
    ];

    // Fluent draws the check mark with a fixed white brush; on a light accent (yellow) it has to
    // follow OnAccent instead.
    private static readonly string[] CheckGlyphKeys =
    [
        "CheckBoxCheckGlyphForegroundChecked",
        "CheckBoxCheckGlyphForegroundCheckedPointerOver",
        "CheckBoxCheckGlyphForegroundCheckedPressed",
        "CheckBoxCheckGlyphForegroundCheckedDisabled",
    ];

    private static readonly Dictionary<string, SolidColorBrush> Brushes = new();
    // What is on screen right now; a transition interrupted midway continues from here.
    private static ThemePalette displayed = ThemePalette.Classic;
    private static DispatcherTimer? timer;
    private static Application? registeredFor;

    // Keeps the app dressed in whichever camera the view model has selected.
    public static void Follow(MainViewModel viewModel)
    {
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Palette))
            {
                Apply(viewModel.Palette, animate: true);
            }
        };
    }

    public static void Apply(ThemePalette palette, bool animate)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        Register(app);
        timer?.Stop();

        var from = displayed;
        if (!animate)
        {
            Paint(app, palette);
            return;
        }

        var started = DateTime.UtcNow;
        timer = new DispatcherTimer { Interval = Frame };
        timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, (DateTime.UtcNow - started) / Duration);
            var eased = 1 - Math.Pow(1 - t, 3);
            Paint(app, Blend(from, palette, eased));
            if (t >= 1)
            {
                timer?.Stop();
            }
        };
        timer.Start();
    }

    private static void Register(Application app)
    {
        if (ReferenceEquals(registeredFor, app))
        {
            return;
        }

        registeredFor = app;
        Brushes.Clear();
        displayed = ThemePalette.Classic;
        foreach (var (key, pick) in Slots)
        {
            var brush = new SolidColorBrush(pick(ThemePalette.Classic));
            Brushes[key] = brush;
            app.Resources[key] = brush;
        }

        foreach (var key in CheckGlyphKeys)
        {
            app.Resources[key] = Brushes["OnAccentBrush"];
        }
    }

    private static void Paint(Application app, ThemePalette palette)
    {
        displayed = palette;
        foreach (var (key, pick) in Slots)
        {
            Brushes[key].Color = pick(palette);
        }

        if (app.Styles.Count > 0 && app.Styles[0] is FluentTheme fluent)
        {
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                if (fluent.Palettes.TryGetValue(variant, out var resources))
                {
                    resources.Accent = palette.Accent;
                }
            }
        }
    }

    private static ThemePalette Blend(ThemePalette a, ThemePalette b, double t) => b with
    {
        Accent = ThemePalette.Mix(a.Accent, b.Accent, t),
        AccentHover = ThemePalette.Mix(a.AccentHover, b.AccentHover, t),
        AccentPressed = ThemePalette.Mix(a.AccentPressed, b.AccentPressed, t),
        OnAccent = ThemePalette.Mix(a.OnAccent, b.OnAccent, t),
        AccentInk = ThemePalette.Mix(a.AccentInk, b.AccentInk, t),
        Header = ThemePalette.Mix(a.Header, b.Header, t),
        OnHeader = ThemePalette.Mix(a.OnHeader, b.OnHeader, t),
        OnHeaderMuted = ThemePalette.Mix(a.OnHeaderMuted, b.OnHeaderMuted, t),
        Surface = ThemePalette.Mix(a.Surface, b.Surface, t),
        Tint = ThemePalette.Mix(a.Tint, b.Tint, t),
        TintBorder = ThemePalette.Mix(a.TintBorder, b.TintBorder, t),
    };
}
