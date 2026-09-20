using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace charmera_importer.Models;

// Every color the app themes, resolved once per camera. The chrome (header, accent, surface tint)
// takes its identity from the camera; text and status colors stay fixed so readability and the
// meaning of "imported / duplicate / error" never depend on which camera is picked.
public sealed record ThemePalette
{
    // Fixed ink used for text on light surfaces and as the dark "on color" option.
    public static readonly Color Ink = Color.Parse("#1A1A1E");
    private static readonly Color BaseSurface = Color.Parse("#F3F3F5");

    // Fills: primary button, checked boxes, progress, completed step badges.
    public required Color Accent { get; init; }
    public required Color AccentHover { get; init; }
    public required Color AccentPressed { get; init; }
    // Text and glyphs drawn on top of Accent.
    public required Color OnAccent { get; init; }
    // Accent as a stroke or icon on white (selection rings): always at least 3:1 against white.
    public required Color AccentInk { get; init; }

    public required Color Header { get; init; }
    public required Color OnHeader { get; init; }
    public required Color OnHeaderMuted { get; init; }

    public required Color Surface { get; init; }
    // Soft call-out background (the "repaired metadata" notes) and its outline.
    public required Color Tint { get; init; }
    public required Color TintBorder { get; init; }

    // The camera's signature stripe, shown under the header. Empty means no stripe.
    public IReadOnlyList<Color> Stripe { get; init; } = Array.Empty<Color>();

    // The original look, used until a camera is chosen: Kodak red on a near-black header.
    public static readonly ThemePalette Classic = new()
    {
        Accent = Color.Parse("#ED1C24"),
        AccentHover = Color.Parse("#D4151C"),
        AccentPressed = Color.Parse("#B01015"),
        OnAccent = Colors.White,
        AccentInk = Color.Parse("#ED1C24"),
        Header = Color.Parse("#17181B"),
        OnHeader = Colors.White,
        OnHeaderMuted = Color.Parse("#9A9AA2"),
        Surface = BaseSurface,
        Tint = Color.Parse("#FFF4D6"),
        TintBorder = Color.Parse("#F5D98B"),
    };

    // Derives the whole palette from the colors a designer would pick by hand: the accent, the header,
    // and the hue the app background leans toward (usually the camera's body, so a gray camera
    // gets a neutral app rather than one tinted by its accent).
    public static ThemePalette FromCamera(Color accent, Color header, Color surfaceTint, params Color[] stripe) => new()
    {
        Accent = accent,
        AccentHover = Mix(accent, Colors.Black, 0.10),
        AccentPressed = Mix(accent, Colors.Black, 0.20),
        OnAccent = BestOn(accent),
        AccentInk = DarkenUntil(accent, Colors.White, 3.0),
        Header = header,
        OnHeader = BestOn(header),
        OnHeaderMuted = MutedOn(header),
        Surface = Mix(BaseSurface, surfaceTint, 0.04),
        Tint = Mix(Colors.White, accent, 0.12),
        TintBorder = Mix(Colors.White, accent, 0.35),
        Stripe = stripe,
    };

    // White or ink, whichever reads better on the given background.
    public static Color BestOn(Color background) =>
        Contrast(Colors.White, background) >= Contrast(Ink, background) ? Colors.White : Ink;

    // Secondary text on the header: the primary text color pulled toward the header, as far as
    // it can go while still passing 4.5:1.
    private static Color MutedOn(Color background)
    {
        var text = BestOn(background);
        var muted = text;
        for (var amount = 0.05; amount <= 0.5; amount += 0.05)
        {
            var candidate = Mix(text, background, amount);
            if (Contrast(candidate, background) < 4.5)
            {
                break;
            }

            muted = candidate;
        }

        return muted;
    }

    private static Color DarkenUntil(Color color, Color against, double ratio)
    {
        var result = color;
        for (var amount = 0.0; amount <= 1.0 && Contrast(result, against) < ratio; amount += 0.02)
        {
            result = Mix(color, Colors.Black, amount);
        }

        return result;
    }

    public static Color Mix(Color from, Color to, double amount)
    {
        static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);
        return Color.FromRgb(Lerp(from.R, to.R, amount), Lerp(from.G, to.G, amount), Lerp(from.B, to.B, amount));
    }

    // WCAG 2.x contrast ratio between two opaque colors (1:1 to 21:1).
    public static double Contrast(Color a, Color b)
    {
        var (light, dark) = (RelativeLuminance(a), RelativeLuminance(b));
        if (light < dark)
        {
            (light, dark) = (dark, light);
        }

        return (light + 0.05) / (dark + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
