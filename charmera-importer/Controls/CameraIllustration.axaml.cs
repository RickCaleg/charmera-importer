using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using charmera_importer.Models;

namespace charmera_importer.Controls;

public partial class CameraIllustration : UserControl
{
    public static readonly StyledProperty<CameraVariant?> VariantProperty =
        AvaloniaProperty.Register<CameraIllustration, CameraVariant?>(nameof(Variant));

    private static readonly Color Dark = Color.Parse("#2A2A2E");
    private static readonly Color Silver = Color.Parse("#C4C9D0");

    private readonly Dictionary<string, Control> artLayers;

    public CameraIllustration()
    {
        InitializeComponent();
        artLayers = new Dictionary<string, Control>
        {
            ["yellow"] = ArtYellow,
            ["red"] = ArtRed,
            ["gray"] = ArtGray,
            ["geometric"] = ArtGeometric,
            ["prism"] = ArtPrism,
            ["blue"] = ArtBlue,
            ["transparent"] = ArtTransparent,
        };
        Render();
    }

    public CameraVariant? Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == VariantProperty)
        {
            Render();
        }
    }

    private void Render()
    {
        var variant = Variant;
        IsVisible = variant is not null;
        if (variant is null)
        {
            return;
        }

        var glass = variant.Id == "transparent";
        var body = variant.Body;

        // The transparent shell lets whatever is behind it show through.
        Front.Background = new SolidColorBrush(body, glass ? 0.45 : 1);
        Front.BorderBrush = new SolidColorBrush(ThemePalette.Mix(body, glass ? Colors.White : Colors.Black, glass ? 0.6 : 0.18));
        TopFace.Background = new SolidColorBrush(ThemePalette.Mix(body, Colors.White, glass ? 0.4 : 0.22), glass ? 0.55 : 1);
        Slit.Background = new SolidColorBrush(ThemePalette.Mix(body, Colors.Black, 0.28), glass ? 0.5 : 1);

        var button = new SolidColorBrush(glass ? Silver : Dark);
        Shutter.Fill = button;
        SmallButton.Fill = button;
        Led.IsVisible = glass;

        foreach (var (id, layer) in artLayers)
        {
            layer.IsVisible = id == variant.Id;
        }
    }
}
