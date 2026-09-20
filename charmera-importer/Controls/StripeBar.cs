using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace charmera_importer.Controls;

// The parallel stripes printed on the Charmera's body, reused as a rule under the app header.
// Each color is one band, top to bottom; no colors means no height at all.
public sealed class StripeBar : Control
{
    public const double BandHeight = 2;

    public static readonly StyledProperty<IReadOnlyList<Color>?> ColorsProperty =
        AvaloniaProperty.Register<StripeBar, IReadOnlyList<Color>?>(nameof(Colors));

    static StripeBar()
    {
        AffectsMeasure<StripeBar>(ColorsProperty);
        AffectsRender<StripeBar>(ColorsProperty);
    }

    public IReadOnlyList<Color>? Colors
    {
        get => GetValue(ColorsProperty);
        set => SetValue(ColorsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, (Colors?.Count ?? 0) * BandHeight);

    public override void Render(DrawingContext context)
    {
        if (Colors is not { } colors)
        {
            return;
        }

        var y = 0.0;
        foreach (var color in colors)
        {
            context.FillRectangle(new ImmutableSolidColorBrush(color), new Rect(0, y, Bounds.Width, BandHeight));
            y += BandHeight;
        }
    }
}
