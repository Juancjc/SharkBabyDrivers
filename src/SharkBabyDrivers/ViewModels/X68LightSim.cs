using SharkBabyDrivers.Core.Devices.X68;

namespace SharkBabyDrivers.ViewModels;

/// <summary>A key's light, 0..1 per channel, already scaled by the effect's intensity and the brightness.</summary>
public readonly record struct LedColor(double R, double G, double B)
{
    public static readonly LedColor Dark = new(0, 0, 0);
    public double Level => Math.Max(R, Math.Max(G, B));
    public LedColor Scale(double k) => new(R * k, G * k, B * k);
    public static LedColor FromRgb(uint rgb) => new(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);

    /// <summary>Fully saturated colour for a hue in turns (0..1 wraps).</summary>
    public static LedColor Hue(double turns)
    {
        var h = X68LightSim.Frac(turns) * 6;
        var x = 1 - Math.Abs(h % 2 - 1);
        return (int)h switch
        {
            0 => new(1, x, 0), 1 => new(x, 1, 0), 2 => new(0, 1, x),
            3 => new(0, x, 1), 4 => new(x, 0, 1), _ => new(1, 0, x),
        };
    }
}

/// <summary>What the keyboard's LEDs are set to, as the lit keyboard drawing needs it.</summary>
/// <param name="Picture">The custom picture's bytes (RGB per matrix slot) when the effect is UserPicture.</param>
public sealed record X68LightState(X68LightMode Mode, int Option, int Speed, int Brightness, uint Color, bool Rainbow, byte[]? Picture)
{
    public static readonly X68LightState Off = new(X68LightMode.Off, 0, 0, 0, 0, false, null);

    /// <param name="device">The keyboard's stored lighting (not pending edits).</param>
    /// <param name="signalColor">While SignalRGB owns the lighting the keyboard shows one streamed colour on every key.</param>
    public static X68LightState From(X68Image device, bool signalRgbOwns, (byte R, byte G, byte B)? signalColor, bool lightsDark)
    {
        if (signalRgbOwns || device.LightMode == X68LightMode.ScreenColor)
            return signalColor is { } c && !lightsDark
                ? new(X68LightMode.AlwaysOn, 0, 0, X68Lighting.MaxBrightness, (uint)(c.R << 16 | c.G << 8 | c.B), false, null)
                : Off;
        var picture = device.LightMode == X68LightMode.UserPicture && device.LightOption < device.UserPics.Length
            ? device.UserPics[device.LightOption] : null;
        return new(device.LightMode, device.LightOption, device.LightSpeed, device.LightBrightness, device.LightColor, device.LightRainbow, picture);
    }
}

/// <summary>
/// An approximation of the firmware effects for the on-screen keyboard: static ones are exact (colour, rainbow,
/// custom picture, brightness); animated ones follow the effect's idea (direction, speed) but not its exact timing.
/// </summary>
public static class X68LightSim
{
    public static bool IsAnimated(X68LightState s) =>
        s.Brightness > 0 && s.Mode is not (X68LightMode.Off or X68LightMode.AlwaysOn or X68LightMode.UserPicture);

    /// <summary>The colour of every cap (same order as <paramref name="caps"/>) at <paramref name="t"/> seconds.</summary>
    public static LedColor[] Frame(X68LightState s, IReadOnlyList<KeyCap> caps, double t)
    {
        var result = new LedColor[caps.Count];
        if (caps.Count == 0 || s.Mode == X68LightMode.Off || s.Brightness <= 0) return result;

        var w = caps.Max(c => c.X + c.Width);
        var h = caps.Max(c => c.Y) + 1;
        var rate = 0.35 + 0.3 * Math.Clamp(s.Speed, 0, X68Lighting.MaxSpeed);
        var fixedColor = LedColor.FromRgb(s.Color);
        LedColor Tint(double hue) => s.Rainbow ? LedColor.Hue(hue) : fixedColor;
        var order = s.Mode == X68LightMode.Snake ? SnakeOrder(caps, w, h, spiral: s.Option == 1) : null;
        var presses = s.Mode is X68LightMode.PressAction or X68LightMode.PressActionOff or X68LightMode.Ripple
            ? Presses(caps.Count, t, rate) : [];

        for (var i = 0; i < caps.Count; i++)
        {
            var cap = caps[i];
            var cx = cap.X + cap.Width / 2;
            var cy = cap.Y + 0.5;
            var u = cx / w;
            var v = cy / h;
            LedColor c;
            switch (s.Mode)
            {
                case X68LightMode.AlwaysOn:
                    c = Tint(u);
                    break;
                case X68LightMode.Breath:
                    var breath = t * rate * 0.5;
                    c = Tint(Math.Floor(breath) / 7).Scale(0.5 - 0.5 * Math.Cos(2 * Math.PI * breath));
                    break;
                case X68LightMode.Neon:
                    c = LedColor.Hue(t * rate * 0.15);
                    break;
                case X68LightMode.Wave:
                    var coord = s.Option switch { 1 => 1 - u, 2 => v, 3 => 1 - v, _ => u };
                    c = Moving(coord, t * rate * 0.4);
                    break;
                case X68LightMode.SineWave:
                    c = Moving(u + 0.12 * Math.Sin(2 * Math.PI * (v * 2 + t * rate * 0.3)), t * rate * 0.4);
                    break;
                case X68LightMode.CircleWave:
                    var angle = Math.Atan2((cy - h / 2) * 2, cx - w / 2) / (2 * Math.PI);
                    c = Moving(s.Option == 1 ? -angle : angle, t * rate * 0.3);
                    break;
                case X68LightMode.Kaleidoscope:
                    var r = Math.Sqrt(Math.Pow(u - 0.5, 2) + Math.Pow((v - 0.5) * h / w, 2)) * 2;
                    c = Moving(s.Option == 1 ? -r : r, t * rate * 0.4);
                    break;
                case X68LightMode.Converge:
                    c = Moving(-Math.Abs(u - 0.5) * 2, t * rate * 0.4);
                    break;
                case X68LightMode.LineWave:
                    var line = Dist01((s.Option == 1 ? 1 - u : u) - t * rate * 0.3);
                    c = Tint(t * 0.1).Scale(Math.Max(0.06, 1 - line / 0.1));
                    break;
                case X68LightMode.Laser:
                    var row = Math.Round(cap.Y);
                    var dot = Frac(t * rate * 0.45 + Hash((int)row));
                    c = Tint(row / 6).Scale(Math.Max(0.04, 1 - Math.Abs(u - dot) / 0.12));
                    break;
                case X68LightMode.Snake:
                    var length = 9.0;
                    var behind = Frac((t * rate * 4 - order![i]) / caps.Count) * caps.Count;
                    c = Tint(order[i] / (double)caps.Count).Scale(behind < length ? 1 - behind / length : 0.05);
                    break;
                case X68LightMode.Raindrop:
                    var drop = t * rate * 0.35 + Hash(cap.Slot);
                    var age = Frac(drop);
                    c = Tint(Hash(cap.Slot * 7 + (int)Math.Floor(drop))).Scale(age < 0.25 ? 1 - age / 0.25 : 0);
                    break;
                case X68LightMode.Dazzling:
                    var step = Math.Floor(t * rate + Hash(cap.Slot));
                    c = s.Rainbow ? LedColor.Hue(Hash(cap.Slot * 13 + (int)step)) : fixedColor.Scale(0.3 + 0.7 * Hash(cap.Slot + (int)step * 31));
                    break;
                case X68LightMode.RainDown:
                    var column = (int)Math.Round(cx * 2);
                    var fall = Frac(t * rate * 0.3 + Hash(column)) * 1.6 - v;
                    c = Tint(Hash(column * 5)).Scale(fall is >= 0 and < 0.6 ? 1 - fall / 0.6 : 0);
                    break;
                case X68LightMode.Meteor:
                    var lane = (int)Math.Round(cap.Y);
                    var trail = Frac(t * rate * 0.25 + Hash(lane + 40)) * 1.8 - u;
                    c = Tint(lane / 6.0 + t * 0.05).Scale(trail is >= 0 and < 0.8 ? 1 - trail / 0.8 : 0);
                    break;
                case X68LightMode.PressAction:
                case X68LightMode.PressActionOff:
                    var lit = presses.Where(p => p.Key == i).Select(p => Math.Max(0, 1 - p.Age / 1.2)).DefaultIfEmpty(0).Max();
                    c = Tint(u).Scale(s.Mode == X68LightMode.PressAction ? lit : 1 - lit);
                    break;
                case X68LightMode.Ripple:
                    var ring = 0.0;
                    foreach (var p in presses)
                    {
                        var from = caps[p.Key];
                        var dist = Math.Sqrt(Math.Pow(cx - (from.X + from.Width / 2), 2) + Math.Pow(cy - (from.Y + 0.5), 2));
                        ring = Math.Max(ring, Math.Max(0, 1 - Math.Abs(dist - p.Age * rate * 6) / 0.9) * Math.Max(0, 1 - p.Age / 2));
                    }
                    c = Tint(u).Scale(ring);
                    break;
                case X68LightMode.UserPicture:
                    var at = cap.Slot * 3;
                    c = s.Picture is { } pic && at + 2 < pic.Length
                        ? new LedColor(pic[at] / 255.0, pic[at + 1] / 255.0, pic[at + 2] / 255.0)
                        : LedColor.Dark;
                    break;
                default:
                    c = Tint(u);
                    break;
            }
            result[i] = c.Scale(Math.Clamp(s.Brightness, 0, X68Lighting.MaxBrightness) / (double)X68Lighting.MaxBrightness);
        }
        return result;

        // A pattern travelling along coord: rainbow hues, or bright bands of the single colour.
        LedColor Moving(double coord, double shift) => s.Rainbow
            ? LedColor.Hue(coord - shift)
            : fixedColor.Scale(0.12 + 0.88 * (0.5 + 0.5 * Math.Cos(2 * Math.PI * (coord * 1.2 - shift))));
    }

    /// <summary>Simulated key presses for the reactive effects: one every so often on a pseudo-random key.</summary>
    private static List<(int Key, double Age)> Presses(int keys, double t, double rate)
    {
        var period = 0.55 / rate;
        var last = (int)Math.Floor(t / period);
        var list = new List<(int, double)>();
        for (var n = last; n > last - 5 && n >= 0; n--)
            list.Add(((int)(Hash(n + 1000) * keys) % keys, t - n * period));
        return list;
    }

    /// <summary>Position of each cap along the snake's path: rows zig-zag, or rings spiralling in.</summary>
    private static int[] SnakeOrder(IReadOnlyList<KeyCap> caps, double w, double h, bool spiral)
    {
        var rows = (int)Math.Round(h);
        var keyed = caps.Select((c, i) =>
        {
            var row = (int)Math.Round(c.Y);
            var x = c.X + c.Width / 2;
            if (!spiral) return (Index: i, A: (double)row, B: row % 2 == 0 ? x : -x);
            var ring = Math.Min(Math.Min(row, rows - 1 - row), Math.Min(x, w - x));
            var angle = Math.Atan2((c.Y + 0.5 - h / 2) * 2, x - w / 2);
            return (Index: i, A: Math.Floor(ring), B: angle);
        }).OrderBy(k => k.A).ThenBy(k => k.B).ToList();
        var order = new int[caps.Count];
        for (var p = 0; p < keyed.Count; p++) order[keyed[p].Index] = p;
        return order;
    }

    internal static double Frac(double x) => x - Math.Floor(x);
    private static double Dist01(double x) => Math.Abs(Frac(x + 0.5) - 0.5);

    private static double Hash(int n)
    {
        unchecked
        {
            var x = (uint)n * 2654435761u;
            x ^= x >> 15; x *= 2246822519u; x ^= x >> 13;
            return (x & 0xFFFFFF) / (double)0x1000000;
        }
    }
}
