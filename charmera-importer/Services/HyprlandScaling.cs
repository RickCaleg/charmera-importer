using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace charmera_importer.Services;

// Avalonia only ships an X11 backend, so on a Wayland session the app runs through XWayland.
// Hyprland's `xwayland { force_zero_scaling = true }` (the Omarchy default) makes the compositor
// leave XWayland clients unscaled, and Avalonia's X11 backend takes its scale from
// AVALONIA_GLOBAL_SCALE_FACTOR / QT_SCALE_FACTOR / Xft.dpi, never from GDK_SCALE: on a 1.6x
// monitor the UI would be drawn at 1.0x and look tiny. This reads the focused monitor's scale from
// hyprctl and hands it to Avalonia through AVALONIA_GLOBAL_SCALE_FACTOR, only in that exact setup.
// Everything else (Windows, macOS, other compositors, hyprctl missing or failing, an explicit
// user-set scale) is left untouched.
public static class HyprlandScaling
{
    private const string GlobalScaleVariable = "AVALONIA_GLOBAL_SCALE_FACTOR";

    private const double MinFactor = 0.5;
    private const double MaxFactor = 4.0;
    private const double NegligibleDifference = 0.01;
    private static readonly TimeSpan HyprctlTimeout = TimeSpan.FromSeconds(2);

    // Every variable Avalonia's X11 scaling provider consults. If the user set any of them, they
    // chose a scale on purpose and it wins.
    private static readonly string[] AvaloniaScaleVariables =
        { GlobalScaleVariable, "AVALONIA_SCREEN_SCALE_FACTORS" };
    private static readonly string[] QtScaleVariables = { "QT_SCALE_FACTOR", "QT_SCREEN_SCALE_FACTORS" };

    // Call before Avalonia starts: the X11 backend reads the variable once at platform startup.
    public static void Apply()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var factor = DetectFactor(Environment.GetEnvironmentVariable, RunHyprctl);
        if (factor is { } value)
        {
            Environment.SetEnvironmentVariable(GlobalScaleVariable, value.ToString("0.####", CultureInfo.InvariantCulture));
        }
    }

    // Returns the factor to force, or null when the current behavior must stay as is.
    public static double? DetectFactor(Func<string, string?> getEnv, Func<string[], string?> hyprctl)
    {
        if (string.IsNullOrEmpty(getEnv("HYPRLAND_INSTANCE_SIGNATURE")) || HasUserScale(getEnv))
        {
            return null;
        }

        if (ParseForceZeroScaling(hyprctl(new[] { "getoption", "xwayland:force_zero_scaling", "-j" })) != true)
        {
            return null;
        }

        if (ParseFocusedMonitorScale(hyprctl(new[] { "monitors", "-j" })) is not { } scale)
        {
            return null;
        }

        var factor = Math.Clamp(scale, MinFactor, MaxFactor);
        return Math.Abs(factor - 1.0) < NegligibleDifference ? null : factor;
    }

    private static bool HasUserScale(Func<string, string?> getEnv)
    {
        foreach (var name in AvaloniaScaleVariables)
        {
            if (getEnv(name) is not null)
            {
                return true;
            }
        }

        // Avalonia skips the QT_* variables when this is "1", so they are no user scale then.
        if (getEnv("AVALONIA_SCREEN_SCALE_IGNORE_QT") == "1")
        {
            return false;
        }

        foreach (var name in QtScaleVariables)
        {
            if (getEnv(name) is not null)
            {
                return true;
            }
        }

        return false;
    }

    // `hyprctl getoption xwayland:force_zero_scaling -j` -> {"option": "...", "bool": true, "set": true}
    public static bool? ParseForceZeroScaling(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? string.Empty);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("bool", out var value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    // `hyprctl monitors -j` -> [{"name": "eDP-1", "scale": 1.6, "focused": true, ...}, ...]
    public static double? ParseFocusedMonitorScale(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? string.Empty);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var monitor in doc.RootElement.EnumerateArray())
            {
                if (monitor.ValueKind == JsonValueKind.Object
                    && monitor.TryGetProperty("focused", out var focused) && focused.ValueKind == JsonValueKind.True
                    && monitor.TryGetProperty("scale", out var scale) && scale.ValueKind == JsonValueKind.Number
                    && scale.TryGetDouble(out var value) && double.IsFinite(value) && value > 0)
                {
                    return value;
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string? RunHyprctl(string[] args)
    {
        try
        {
            var startInfo = new ProcessStartInfo("hyprctl")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            // Both pipes are drained asynchronously so a chatty hyprctl cannot block on a full pipe.
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(HyprctlTimeout))
            {
                process.Kill();
                return null;
            }

            return process.ExitCode == 0 ? output.GetAwaiter().GetResult() : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException
                                       or System.IO.IOException)
        {
            // hyprctl is not installed (or not runnable): leave the default scaling alone.
            return null;
        }
    }
}
