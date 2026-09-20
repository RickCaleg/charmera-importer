using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace charmera_importer.Models;

// One of the Charmera's factory looks. The artwork lives in Controls/CameraIllustration.axaml,
// keyed by Id; the palette drives the app's theme when the user picks their camera.
public sealed record CameraVariant(string Id, string NameKey, Color Body, ThemePalette Palette)
{
    // The first collection ("1987"): six colorways plus a secret transparent one. Names and looks
    // follow the retail listings; the drawings are original vector illustrations of them.
    public static readonly CameraVariant Yellow = new("yellow", "Camera_Yellow", Color.Parse("#FFBC0D"),
        ThemePalette.FromCamera(Color.Parse("#FFBC0D"), Color.Parse("#FFBC0D"),
            Color.Parse("#FFBC0D"),
            Color.Parse("#1A1A1E"), Color.Parse("#2A2C86"), Color.Parse("#5B2A8C"),
            Color.Parse("#D62828"), Color.Parse("#F77F00")));

    public static readonly CameraVariant Red = new("red", "Camera_Red", Color.Parse("#E5262B"),
        ThemePalette.FromCamera(Color.Parse("#D9232A"), Color.Parse("#D9232A"),
            Color.Parse("#E5262B"),
            Color.Parse("#D9232A"), Colors.White, Color.Parse("#D9232A"), Colors.White, Color.Parse("#D9232A")));

    public static readonly CameraVariant Gray = new("gray", "Camera_Gray", Color.Parse("#B9BCC1"),
        ThemePalette.FromCamera(Color.Parse("#C81F63"), Color.Parse("#B9BCC1"),
            Color.Parse("#9EA3AB"),
            Color.Parse("#2E2A86"), Color.Parse("#D6246E"), Color.Parse("#E8362E"),
            Color.Parse("#F57F17"), Color.Parse("#FDB515"), Color.Parse("#1A1A1A")));

    public static readonly CameraVariant Geometric = new("geometric", "Camera_Geometric", Color.Parse("#EEE9DC"),
        ThemePalette.FromCamera(Color.Parse("#2B4BA2"), Color.Parse("#EEE9DC"),
            Color.Parse("#E6D5A0"),
            Color.Parse("#D62828"), Color.Parse("#2B4BA2"), Color.Parse("#FDB515")));

    public static readonly CameraVariant Prism = new("prism", "Camera_Prism", Color.Parse("#1D1D20"),
        ThemePalette.FromCamera(Color.Parse("#2B2B30"), Color.Parse("#1D1D20"),
            Color.Parse("#8A8A94"),
            Color.Parse("#E5262B"), Color.Parse("#F58220"), Color.Parse("#FDB913"),
            Color.Parse("#3DAE49"), Color.Parse("#1FA3D9"), Color.Parse("#2B4BA2")));

    public static readonly CameraVariant Blue = new("blue", "Camera_Blue", Color.Parse("#2547B4"),
        ThemePalette.FromCamera(Color.Parse("#2547B4"), Color.Parse("#2547B4"),
            Color.Parse("#2547B4"),
            Colors.White, Color.Parse("#1B2F80"), Color.Parse("#2BB7D8"), Colors.White));

    // The secret edition (1 in 48 in the blind boxes).
    public static readonly CameraVariant Transparent = new("transparent", "Camera_Transparent", Color.Parse("#E6EAEE"),
        ThemePalette.FromCamera(Color.Parse("#23794A"), Color.Parse("#262B31"),
            Color.Parse("#8FA3B5"),
            Color.Parse("#C9CED4"), Color.Parse("#5DA86E"), Color.Parse("#D4E836")));

    public static readonly IReadOnlyList<CameraVariant> All =
        [Yellow, Red, Gray, Geometric, Prism, Blue, Transparent];

    public static CameraVariant? FromId(string? id) => All.FirstOrDefault(v => v.Id == id);
}
