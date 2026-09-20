using charmera_importer.Services;

namespace charmera_importer.Tests;

public class HyprlandScalingTests
{
    private const string ForceZeroOn = """{"option": "xwayland:force_zero_scaling", "bool": true, "set": true }""";
    private const string ForceZeroOff = """{"option": "xwayland:force_zero_scaling", "bool": false, "set": false }""";

    private static string Monitors(double focusedScale, double otherScale = 1.0) =>
        $$"""
        [
          {"name": "HDMI-A-1", "scale": {{otherScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "focused": false},
          {"name": "eDP-1", "scale": {{focusedScale.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "focused": true}
        ]
        """;

    private static Func<string, string?> Env(params (string Name, string Value)[] vars)
    {
        var map = vars.ToDictionary(v => v.Name, v => v.Value);
        return name => map.GetValueOrDefault(name);
    }

    private static readonly (string, string) Hyprland = ("HYPRLAND_INSTANCE_SIGNATURE", "abc_123");

    private static Func<string[], string?> Hyprctl(string? forceZero, string? monitors) =>
        args => args[0] == "getoption" ? forceZero : monitors;

    [Fact]
    public void Uses_the_focused_monitor_scale_under_hyprland_with_force_zero_scaling()
    {
        var factor = HyprlandScaling.DetectFactor(Env(Hyprland), Hyprctl(ForceZeroOn, Monitors(1.6, 2.0)));
        Assert.Equal(1.6, factor);
    }

    [Fact]
    public void Does_nothing_outside_hyprland()
    {
        Assert.Null(HyprlandScaling.DetectFactor(Env(), Hyprctl(ForceZeroOn, Monitors(1.6))));
    }

    [Fact]
    public void Does_nothing_when_force_zero_scaling_is_off()
    {
        // Hyprland then upscales XWayland itself; forcing a factor on top would double the scale.
        Assert.Null(HyprlandScaling.DetectFactor(Env(Hyprland), Hyprctl(ForceZeroOff, Monitors(1.6))));
    }

    [Theory]
    [InlineData("AVALONIA_GLOBAL_SCALE_FACTOR")]
    [InlineData("AVALONIA_SCREEN_SCALE_FACTORS")]
    [InlineData("QT_SCALE_FACTOR")]
    [InlineData("QT_SCREEN_SCALE_FACTORS")]
    public void Respects_a_scale_the_user_already_set(string variable)
    {
        Assert.Null(HyprlandScaling.DetectFactor(Env(Hyprland, (variable, "1.25")), Hyprctl(ForceZeroOn, Monitors(1.6))));
    }

    [Fact]
    public void Qt_variables_do_not_count_when_avalonia_ignores_them()
    {
        var factor = HyprlandScaling.DetectFactor(
            Env(Hyprland, ("QT_SCALE_FACTOR", "1.25"), ("AVALONIA_SCREEN_SCALE_IGNORE_QT", "1")),
            Hyprctl(ForceZeroOn, Monitors(1.6)));
        Assert.Equal(1.6, factor);
    }

    [Fact]
    public void Leaves_a_1x_monitor_alone()
    {
        Assert.Null(HyprlandScaling.DetectFactor(Env(Hyprland), Hyprctl(ForceZeroOn, Monitors(1.0))));
        Assert.Null(HyprlandScaling.DetectFactor(Env(Hyprland), Hyprctl(ForceZeroOn, Monitors(1.005))));
    }

    [Theory]
    [InlineData(0.1, 0.5)]
    [InlineData(9.0, 4.0)]
    [InlineData(1.25, 1.25)]
    public void Clamps_the_factor_to_a_sane_range(double scale, double expected)
    {
        Assert.Equal(expected, HyprlandScaling.DetectFactor(Env(Hyprland), Hyprctl(ForceZeroOn, Monitors(scale))));
    }

    [Fact]
    public void Does_nothing_when_hyprctl_is_missing_or_prints_garbage()
    {
        Assert.Null(HyprlandScaling.DetectFactor(Env(Hyprland), Hyprctl(null, null)));
        Assert.Null(HyprlandScaling.DetectFactor(Env(Hyprland), Hyprctl(ForceZeroOn, null)));
        Assert.Null(HyprlandScaling.DetectFactor(Env(Hyprland), Hyprctl("not json", "{")));
    }

    [Fact]
    public void Does_nothing_without_a_focused_monitor()
    {
        const string noFocus = """[{"name": "eDP-1", "scale": 1.6, "focused": false}]""";
        Assert.Null(HyprlandScaling.ParseFocusedMonitorScale(noFocus));
        Assert.Null(HyprlandScaling.ParseFocusedMonitorScale("[]"));
    }

    [Theory]
    [InlineData("""{"scale": 0, "focused": true}""")]
    [InlineData("""{"scale": "1.6", "focused": true}""")]
    public void Ignores_invalid_monitor_scales(string monitor)
    {
        Assert.Null(HyprlandScaling.ParseFocusedMonitorScale($"[{monitor}]"));
    }

    [Fact]
    public void Reads_force_zero_scaling_from_the_bool_field()
    {
        Assert.True(HyprlandScaling.ParseForceZeroScaling(ForceZeroOn));
        Assert.False(HyprlandScaling.ParseForceZeroScaling(ForceZeroOff));
        // hyprctl exposes booleans in "bool"; an "int" field must not be mistaken for it.
        Assert.Null(HyprlandScaling.ParseForceZeroScaling("""{"option": "x", "int": 1}"""));
    }
}
