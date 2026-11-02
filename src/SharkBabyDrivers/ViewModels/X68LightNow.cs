using SharkBabyDrivers.Core.Devices.X68;
using SharkBabyDrivers.Localization;

namespace SharkBabyDrivers.ViewModels;

/// <summary>
/// What the keyboard is showing right now, in words and colour: the effect stored on the device (not the editor's
/// pending values) or, while SignalRGB owns the lighting, the last colour streamed to it.
/// </summary>
/// <param name="Effect">Effect name (with variant when it has one).</param>
/// <param name="Detail">Brightness / speed line; empty when the lights are off.</param>
public sealed record X68LightNow(string Effect, string Detail)
{
    public static X68LightNow From(X68Image device, bool signalRgbOwns, (byte R, byte G, byte B)? signalColor)
    {
        if (signalRgbOwns || device.LightMode == X68LightMode.ScreenColor)
        {
            var hex = signalColor is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null;
            return new X68LightNow(Loc.T("light_now_signal"), hex ?? Loc.T("light_now_signal_waiting"));
        }

        var mode = device.LightMode;
        var info = X68Lighting.Modes.FirstOrDefault(m => m.Mode == mode);
        var effect = Label($"klm_{mode}", mode.ToString());
        if (info is { Options: > 0 } && device.LightOption < info.Options)
            effect += " · " + Label($"klo_{mode}_{device.LightOption}", $"{device.LightOption + 1}");
        if (mode == X68LightMode.Off) return new X68LightNow(effect, "");

        var hasColor = info?.HasColor ?? false;
        var rainbow = hasColor && device.LightRainbow;
        var color = hasColor && !rainbow ? $"#{device.LightColor:X6}" : null;
        var parts = new List<string>();
        if (rainbow) parts.Add(Loc.T("light_now_rainbow"));
        else if (color is not null) parts.Add(color);
        parts.Add(Loc.F("light_now_brightness", device.LightBrightness, X68Lighting.MaxBrightness));
        if (info?.HasSpeed ?? false) parts.Add(Loc.F("light_now_speed", device.LightSpeed, X68Lighting.MaxSpeed));
        return new X68LightNow(effect, string.Join(" · ", parts));
    }

    private static string Label(string key, string fallback) => Loc.I[key] is var t && t != key ? t : fallback;
}
