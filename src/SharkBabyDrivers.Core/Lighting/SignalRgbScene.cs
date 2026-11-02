using SharkBabyDrivers.Core.Devices.X68;

namespace SharkBabyDrivers.Core.Lighting;

/// <summary>
/// What the keyboard runs to look like SignalRGB's scene: the host-driven live colour (exact, one colour for every
/// key) or one of the firmware's own effects (several colours and movement, approximated).
/// </summary>
public sealed record SceneLook(bool Live, X68LightMode Mode, int Option, int Speed, int Brightness, bool Rainbow, uint Color)
{
    public static readonly SceneLook LiveColor = new(true, X68LightMode.ScreenColor, 0, 0, X68Lighting.MaxBrightness, false, 0);

    /// <summary>The firmware effects this app writes to imitate SignalRGB.</summary>
    public static bool IsImitationMode(X68LightMode mode) =>
        mode is X68LightMode.Wave or X68LightMode.Raindrop or X68LightMode.RainDown or X68LightMode.CircleWave;

    /// <summary>
    /// The look of lighting bytes read from the keyboard when they hold an effect this app writes to imitate
    /// SignalRGB, so a reconnection keeps it instead of rewriting it; otherwise null.
    /// </summary>
    public static SceneLook? FromLed(byte[] led)
    {
        if (led.Length < 8 || !X68Lighting.IsPlausibleEffect(led)) return null;
        var img = new X68Image();
        led.AsSpan(0, 8).CopyTo(img.Led);
        if (!IsImitationMode(img.LightMode)) return null;
        return new SceneLook(false, img.LightMode, img.LightOption, img.LightSpeed, img.LightBrightness, img.LightRainbow,
            img.LightRainbow ? 0 : img.LightColor);
    }

    /// <summary>Effect, direction and rainbow: a change here is worth a write soon; speed, brightness, colour are details.</summary>
    public string Kind => Live ? "live" : $"{Mode}/{Option}/{(Rainbow ? "rainbow" : "colour")}";

    /// <summary>
    /// Looks close enough that switching between them is only a detail: the same rainbow choice with raindrops
    /// and rain falling (scenes with drops falling now and then are read as one or the other).
    /// </summary>
    public bool SameFamily(SceneLook other) =>
        Kind == other.Kind
        || (!Live && !other.Live && Rainbow == other.Rainbow
            && Mode is X68LightMode.Raindrop or X68LightMode.RainDown && other.Mode is X68LightMode.Raindrop or X68LightMode.RainDown);

    /// <summary>Equal for looks that deserve the same flash write (colours compared by 30° hue sector).</summary>
    public string Signature => Live ? "live" : $"{Kind}/{Speed}/{Brightness}/{(Rainbow ? "" : $"hue{HueSector(Color)}")}";

    /// <summary>The 0x87/0x07 lighting bytes for a firmware effect, based on the keyboard's current ones.</summary>
    public byte[] LedBytes(byte[] current)
    {
        var img = new X68Image();
        current.AsSpan(0, Math.Min(current.Length, img.Led.Length)).CopyTo(img.Led);
        img.LightMode = Mode;
        img.LightOption = Option;
        img.LightSpeed = Speed;
        img.LightBrightness = Brightness;
        // The colour setter keeps a rainbow flag that is already on: clear it first, or a rainbow stays a rainbow.
        img.LightRainbow = Rainbow;
        if (!Rainbow) img.LightColor = Color;
        return img.Led;
    }

    public override string ToString() => Live ? "live" : $"{Mode} opt={Option} speed={Speed} bright={Brightness} {(Rainbow ? "rainbow" : $"#{Color:X6}")}";

    internal static int HueSector(uint rgb)
    {
        var (h, s, _) = SignalRgbScene.Hsv(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);
        return s < 0.25 ? -1 : (int)(h * 12) % 12;
    }
}

/// <summary>
/// Reads SignalRGB's per-key colours over the last few seconds and picks the closest look the stock firmware can
/// show (validated against recordings of SignalRGB's own effects, see the tests):
/// <list type="bullet">
/// <item>one colour on (almost) every key, also breathing, flashing or colour cycling → the exact live colour;</item>
/// <item>a pattern moving across the board → the firmware wave with that direction and speed; sparse things falling
/// (rain, meteors) → the firmware rain-down effect; a pattern turning around the centre → the firmware circles;</item>
/// <item>things lighting up and fading with no direction (fireflies, bubbles, fire) → the firmware raindrops;</item>
/// <item>a still rainbow → a very slow rainbow wave; any other still picture → the live colour.</item>
/// </list>
/// The colour is the one that stands out from the background (the wave, not what it passes over). The rainbow variant
/// (the firmware's only multi-colour one) when the hues lit at the same time cover most of the colour circle, or when
/// what stands out comes in several clearly different colours (multi-colour drops); a background with one colour
/// passing over it stays one colour, even with the blends between them. Thread-safe.
/// </summary>
public sealed class SignalRgbScene
{
    public const int WindowMs = 3000;
    private const int W = SignalRgbKeyMap.GridWidth, H = SignalRgbKeyMap.GridHeight;

    private readonly record struct Rgb(double R, double G, double B)
    {
        public double V => Math.Max(R, Math.Max(G, B));
        public double Dist(Rgb o) => (Math.Abs(R - o.R) + Math.Abs(G - o.G) + Math.Abs(B - o.B)) / 3;
    }

    /// <summary>Keys in key-map order and the same colours on the 16 × 6 grid (null = no key there).</summary>
    private sealed record Frame(long T, Rgb?[] Keys, Rgb?[] Cells);

    private readonly object _lock = new();
    private readonly List<Frame> _frames = new();
    private readonly IReadOnlyList<SignalRgbKey> _map;
    private readonly Dictionary<int, int> _indexBySlot;
    private readonly (int Bin, double Weight)[] _angle;
    // Hues of the background over the last ~15 s (exponential memory) of the moving scenes: when the whole board
    // flows through the colour circle (rainbow rings, rainbow rising), the scene is a rainbow even though only a few
    // hues are lit at any moment. A constant background with a colour-cycling wave passing over it is not.
    private readonly double[] _bgHues = new double[12];
    private long _bgHuesAt;

    public SignalRgbScene(IReadOnlyList<SignalRgbKey>? map = null)
    {
        _map = map ?? SignalRgbKeyMap.Keys;
        _indexBySlot = _map.Select((k, i) => (k.Slot, i)).ToDictionary(p => p.Slot, p => p.i);
        // Angle of each key around the centre (rows count double: keys are wider than tall on the 16 × 6 grid).
        _angle = _map.Select(k =>
        {
            var dx = k.X + 0.5 - W / 2.0;
            var dy = (k.Y + 0.5 - H / 2.0) * 2;
            var a = (Math.Atan2(dy, dx) / (2 * Math.PI) + 1) % 1;
            return ((int)(a * AngleBins) % AngleBins, Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / 2));
        }).ToArray();
    }

    private const int AngleBins = 16;

    /// <summary>Diagnostics of the last decision (for lighting.log).</summary>
    public string LastDetail { get; private set; } = "";

    public void Clear()
    {
        lock (_lock)
        {
            _frames.Clear();
            Array.Clear(_bgHues);
            _bgHuesAt = 0;
        }
    }

    public void Add(KeyColorsFrame frame)
    {
        var keys = new Rgb?[_map.Count];
        var cells = new Rgb?[W * H];
        foreach (var (slot, c) in frame.Keys)
        {
            if (!_indexBySlot.TryGetValue(slot, out var i)) continue;
            var rgb = new Rgb(c.R / 255.0, c.G / 255.0, c.B / 255.0);
            keys[i] = rgb;
            cells[_map[i].Y * W + _map[i].X] = rgb;
        }
        lock (_lock)
        {
            if (_frames.Count > 0 && _frames[^1].T >= frame.ReceivedAt) return;
            _frames.Add(new Frame(frame.ReceivedAt, keys, cells));
            _frames.RemoveAll(x => frame.ReceivedAt - x.T > WindowMs);
        }
    }

    /// <summary>The look for the frames of the last <see cref="WindowMs"/>; null while there is too little data.</summary>
    public SceneLook? Decide(long now)
    {
        Frame[] frames;
        lock (_lock) frames = _frames.Where(f => now - f.T <= WindowMs).ToArray();
        if (frames.Length < 6 || frames[^1].T - frames[0].T < 1000) { LastDetail = $"frames={frames.Length}"; return null; }

        // Frames that are (almost) dark say nothing about uniformity (breathing effects pass through black).
        var bright = frames.Where(f => f.Keys.OfType<Rgb>().Any(k => k.V >= 0.1)).ToArray();
        var uniform = bright.Length == 0 ? 1 : bright.Count(IsUniform) / (double)bright.Length;
        var activity = Activity(frames);
        if (uniform >= 0.7)
        {
            LastDetail = FormattableString.Invariant($"frames={frames.Length} uniform={uniform:0.00} activity={activity:0.000}");
            return SceneLook.LiveColor;
        }

        var p = Palette(frames);
        var flowing = RememberBackground(now, p.BackgroundShare);
        // A flowing background counts as a rainbow only without a flat background: with ~80 % of the keys in one
        // (slowly cycling) colour and a band of another passing, it is "a background and a passing colour".
        var rainbow = p.Rainbow || p.MultiAccent || (flowing && p.Spots >= 0.3);
        // Slow patterns are measured between frames ~0.3 s apart, fast ones between consecutive frames; on the
        // colours themselves and on how much each key stands out from the background (small things moving over a
        // still background: rain, stars), keeping the more convincing measurement.
        var m = Best(Pick(Motion(frames, 300, accent: false), Motion(frames, 100, accent: false)),
                     Pick(Motion(frames, 300, accent: true), Motion(frames, 100, accent: true)));
        var r = Rotation(frames);
        var breathing = Breathing(frames);
        LastDetail = FormattableString.Invariant(
            $"frames={frames.Length} uniform={uniform:0.00} rainbow={rainbow}({p.RainbowShare:0.00},{p.MultiAccent},{flowing}) color=#{p.Color:X6} bright={p.Brightness} spots={p.Spots:0.00} v=({m.Vx:0.0},{m.Vy:0.0})({m.Gain:0.00},{m.Agree:0.00}) w={r.W:0.00}({r.Gain:0.00},{r.Agree:0.00}) breath={breathing:0.00} activity={activity:0.000}");

        var color = rainbow ? 0 : p.Color;
        SceneLook Look(X68LightMode mode, int option, int speed) => new(false, mode, option, speed, p.Brightness, rainbow, color);

        if (m.Gain >= 0.3 && m.Agree >= 0.75 && (Math.Abs(m.Vx) >= 0.4 || Math.Abs(m.Vy) >= 0.3))
        {
            var horizontal = Math.Abs(m.Vx) / W >= Math.Abs(m.Vy) / H;
            if (!horizontal && m.Vy > 0 && p.Spots < 0.45) return Look(X68LightMode.RainDown, 0, SpeedLevel(m.Vy / H));
            var option = horizontal ? (m.Vx > 0 ? 0 : 1) : (m.Vy > 0 ? 2 : 3);
            return Look(X68LightMode.Wave, option, SpeedLevel(horizontal ? Math.Abs(m.Vx) / W : Math.Abs(m.Vy) / H));
        }
        if (r.Gain >= 0.3 && r.Agree >= 0.75 && Math.Abs(r.W) >= 0.03)
            return Look(X68LightMode.CircleWave, r.W > 0 ? 1 : 0, SpeedLevel(Math.Abs(r.W) * 2));
        if (breathing >= 0.5) return Look(X68LightMode.Breath, 0, 2);
        if (activity >= 0.03) return Look(X68LightMode.Raindrop, 0, SpeedLevel(activity * 3));
        // A still picture: only a real rainbow becomes the (very slow) rainbow wave; anything else stays live.
        if (p.Rainbow) return Look(X68LightMode.Wave, 0, 0);
        return SceneLook.LiveColor;
    }

    /// <summary>
    /// Adds this window's background hues to the memory of the last ~15 s; true when they cover most of the colour
    /// circle (no gap wider than 120°) and no hue dominates, i.e. the whole board flows through the rainbow. A steady
    /// background that a wide colour-cycling wave sometimes covers keeps dominating, so it is not a rainbow.
    /// </summary>
    private bool RememberBackground(long now, double[] share)
    {
        lock (_lock)
        {
            var keep = _bgHuesAt == 0 ? 0 : Math.Exp(-(now - _bgHuesAt) / 5000.0);
            _bgHuesAt = now;
            for (var i = 0; i < 12; i++) _bgHues[i] = _bgHues[i] * keep + share[i] * (1 - keep);
            var total = _bgHues.Sum();
            return total > 0 && _bgHues.Max() / total < 0.35 && LargestGap(_bgHues.Select(h => h / total >= 0.04).ToArray()) <= 4;
        }
    }

    /// <summary>
    /// How strongly the whole board brightens and dims together (0 = steady): the swing of the mean brightness over
    /// the window relative to its peak, counted only when the board is mostly lit at its peak and the swing is large
    /// in absolute terms (a few keys going on and off — fireflies, a snake — are not breathing).
    /// </summary>
    private static double Breathing(Frame[] frames)
    {
        var means = frames.Select(f => f.Keys.OfType<Rgb>().Select(k => k.V).DefaultIfEmpty(0).Average()).ToList();
        var max = means.Max();
        var peak = frames[means.IndexOf(max)].Keys.OfType<Rgb>().ToList();
        var litAtPeak = peak.Count == 0 ? 0 : peak.Count(k => k.V >= 0.2) / (double)peak.Count;
        if (max - means.Min() < 0.25 || litAtPeak < 0.6) return 0;
        return (max - means.Min()) / max;
    }

    /// <summary>The more convincing of two motion measurements.</summary>
    private static Move Best(Move a, Move b)
    {
        static double Score(Move m) => m.Agree >= 0.75 ? m.Gain : m.Gain * m.Agree * 0.5;
        return Score(b) > Score(a) ? b : a;
    }

    /// <summary>Firmware speed level (0..4) for a pattern crossing the board this many times per second.</summary>
    public static int SpeedLevel(double crossingsPerSecond) => crossingsPerSecond switch
    {
        < 0.12 => 0,
        < 0.22 => 1,
        < 0.38 => 2,
        < 0.6 => 3,
        _ => 4,
    };

    private static Rgb Median(IEnumerable<Rgb> colors)
    {
        var list = colors.ToList();
        if (list.Count == 0) return default;
        double Mid(Func<Rgb, double> f) { var v = list.Select(f).OrderBy(x => x).ToList(); return v[v.Count / 2]; }
        return new Rgb(Mid(c => c.R), Mid(c => c.G), Mid(c => c.B));
    }

    /// <summary>One colour on the whole board: at most 2 % of the keys clearly differ from the median colour.</summary>
    private static bool IsUniform(Frame f)
    {
        var keys = f.Keys.OfType<Rgb>().ToList();
        if (keys.Count == 0) return true;
        var bg = Median(keys);
        return keys.Count(k => k.Dist(bg) > 0.15) <= keys.Count * 0.02;
    }

    /// <summary>Share of keys whose colour clearly changed between frames ~1 s apart (things lighting up or fading).</summary>
    private static double Activity(Frame[] frames)
    {
        var shares = new List<double>();
        for (var i = 0; i < frames.Length; i++)
        {
            var j = Array.FindIndex(frames, i + 1, f => f.T - frames[i].T >= 1000);
            if (j < 0) break;
            int changed = 0, n = 0;
            for (var k = 0; k < frames[i].Keys.Length; k++)
                if (frames[i].Keys[k] is { } a && frames[j].Keys[k] is { } b)
                {
                    n++;
                    if (a.Dist(b) > 0.15) changed++;
                }
            if (n > 0) shares.Add(changed / (double)n);
        }
        return shares.Count == 0 ? 0 : shares.Average();
    }

    /// <summary>
    /// The colours: the background is each frame's median colour and the accent the keys that clearly differ from it
    /// (the wave, the drops; weighted towards the keys that differ most); rainbow when, in most frames, the saturated
    /// lit keys show hues with no gap wider than 120° around the colour circle; brightness from the brightest keys;
    /// Spots = share of keys standing out from the background; BackgroundShare = hues of the background.
    /// </summary>
    private static (bool Rainbow, double RainbowShare, bool MultiAccent, uint Color, int Brightness, double Spots, double[] BackgroundShare) Palette(Frame[] frames)
    {
        var background = new double[12];
        var steadyAccent = new double[12];
        int spots = 0;
        // The usual background of the window: frames where a wide band covers most keys (and so becomes the median)
        // do not count for the multi-colour test, or the background would look like a second accent colour.
        var typical = Median(frames.Select(f => Median(f.Keys.OfType<Rgb>())));
        var accent = new double[12];
        var accentColor = new Rgb[12];
        var any = new double[12];
        var anyColor = new Rgb[12];
        var values = new List<double>();
        int rainbowFrames = 0, colourFrames = 0, keysSeen = 0;
        foreach (var f in frames)
        {
            var keys = f.Keys.OfType<Rgb>().ToList();
            var bg = Median(keys);
            var (bh, bs, bv) = Hsv(bg.R, bg.G, bg.B);
            if (bv >= 0.2 && bs >= 0.3) background[(int)(bh * 12) % 12] += 1;
            var present = new double[12];
            var steady = bg.Dist(typical) <= 0.15;
            foreach (var c in keys)
            {
                var (h, s, v) = Hsv(c.R, c.G, c.B);
                values.Add(v);
                keysSeen++;
                if (c.Dist(bg) > 0.15) spots++;
                if (v < 0.15 || s < 0.3) continue;
                var b = (int)(h * 12) % 12;
                present[b] += 1;
                any[b] += v;
                anyColor[b] = new Rgb(anyColor[b].R + c.R, anyColor[b].G + c.G, anyColor[b].B + c.B);
                var d = c.Dist(bg);
                if (d <= 0.15) continue;
                // The keys that differ most from the background are the core of the wave / drop, not its blends.
                var w = v * d * d;
                accent[b] += w;
                if (steady) steadyAccent[b] += w;
                accentColor[b] = new Rgb(accentColor[b].R + c.R * w, accentColor[b].G + c.G * w, accentColor[b].B + c.B * w);
            }
            var total = present.Sum();
            if (total <= 0) continue; // dark or grey frame: says nothing about the hues
            colourFrames++;
            if (LargestGap(present.Select(x => x / total >= 0.03).ToArray()) <= 4) rainbowFrames++;
        }
        values.Sort();
        var p90 = values.Count == 0 ? 0 : values[(int)Math.Min(values.Count - 1, Math.Round(values.Count * 0.9))];
        var brightness = Math.Clamp((int)Math.Round(p90 * X68Lighting.MaxBrightness), 1, X68Lighting.MaxBrightness);
        var rainbowShare = colourFrames == 0 ? 0 : rainbowFrames / (double)colourFrames;

        var (bins, sums) = accent.Sum() > 0 ? (accent, accentColor) : (any, anyColor);
        uint color = 0xFFFFFF;
        if (bins.Sum() > 0)
        {
            var top = Array.IndexOf(bins, bins.Max());
            var c = sums[top];
            var max = c.V;
            uint Channel(double x) => (uint)Math.Round(Math.Clamp(x / max, 0, 1) * 255);
            color = Channel(c.R) << 16 | Channel(c.G) << 8 | Channel(c.B);
        }
        var bgTotal = background.Sum();
        // The firmware's only multi-colour variant is the rainbow: use it when what stands out from the usual
        // background (drops, spots, bands) comes in two or more clearly different colours (spread around the colour circle), e.g. cyan,
        // violet and magenta drops over dark indigo. One colour passing over a background stays one colour, also when
        // a wide band sometimes covers most keys.
        var multiAccent = MultiColour(steadyAccent);
        return (rainbowShare >= 0.5, rainbowShare, multiAccent, color, brightness, keysSeen == 0 ? 0 : spots / (double)keysSeen,
            background.Select(x => bgTotal > 0 ? x / bgTotal : 0).ToArray());
    }

    /// <summary>
    /// True when the colours are clearly spread around the colour circle: circular variance (1 − length of the
    /// weighted mean direction) ≥ 0.3, i.e. two equal colours ≥ ~90° apart, or many hues. One colour with the blends
    /// at its edges stays well below (the blends weigh little and lie close to it).
    /// </summary>
    private static bool MultiColour(double[] sectors)
    {
        var total = sectors.Sum();
        if (total <= 0) return false;
        double x = 0, y = 0;
        for (var i = 0; i < 12; i++)
        {
            var a = (i + 0.5) / 12 * 2 * Math.PI;
            x += sectors[i] * Math.Cos(a);
            y += sectors[i] * Math.Sin(a);
        }
        return 1 - Math.Sqrt(x * x + y * y) / total >= 0.3;
    }

    /// <summary>Widest run of absent 30° sectors around the circle (12 = nothing present).</summary>
    private static int LargestGap(bool[] present)
    {
        if (!present.Any(x => x)) return 12;
        int best = 0, run = 0;
        for (var i = 0; i < 24; i++)
        {
            run = present[i % 12] ? 0 : run + 1;
            best = Math.Max(best, Math.Min(run, 12));
        }
        return best;
    }

    private readonly record struct Move(double Vx, double Vy, double Gain, double Agree);

    /// <summary>The slow measurement when it is reliable and not near its limit; else the fast one when reliable.</summary>
    private static Move Pick(Move slow, Move fast)
    {
        static bool Reliable(Move m) => m.Agree >= 0.75 && m.Gain >= 0.3;
        var nearLimit = Math.Abs(slow.Vx) >= 7 || Math.Abs(slow.Vy) >= 2.5;
        if (Reliable(slow) && !nearLimit) return slow;
        if (Reliable(fast)) return fast;
        return Reliable(slow) || slow.Gain >= fast.Gain ? slow : fast;
    }

    /// <summary>
    /// Velocity of the whole picture (cells per second, + = right / down) from the 2-D shift that best explains a
    /// frame <paramref name="lagMs"/> later, compared over the same central cells for every shift. Gain = how much
    /// better that shift is than none and than the other shifts (random sparkles fit every shift about equally
    /// badly); Agree = share of measurements moving the same way along the main axis.
    /// </summary>
    private static Move Motion(Frame[] frames, int lagMs, bool accent)
    {
        const int mx = 3, my = 1;
        // accent: each cell is how much it stands out from its frame's median colour (grey level), so a still
        // background drops out and only the things moving over it count.
        Rgb?[] Cells(Frame f)
        {
            if (!accent) return f.Cells;
            var bg = Median(f.Keys.OfType<Rgb>());
            return f.Cells.Select(c => c is { } x ? new Rgb(x.Dist(bg), x.Dist(bg), x.Dist(bg)) : (Rgb?)null).ToArray();
        }
        var shifts = new List<(double Dx, double Dy)>();
        var gains = new List<double>();
        var pairs = 0;
        for (var i = 0; i < frames.Length; i++)
        {
            var j = Array.FindIndex(frames, i + 1, f => f.T - frames[i].T >= lagMs);
            if (j < 0 || frames[j].T - frames[i].T > lagMs + 250) continue;
            pairs++;
            var dt = (frames[j].T - frames[i].T) / 1000.0;
            var costs = new double[2 * my + 1, 2 * mx + 1];
            for (var dy = -my; dy <= my; dy++)
                for (var dx = -mx; dx <= mx; dx++)
                    costs[dy + my, dx + mx] = GridCost(Cells(frames[i]), Cells(frames[j]), dx, dy, mx, my);
            var c0 = costs[my, mx];
            if (double.IsNaN(c0) || c0 < 1e-4) { gains.Add(0); shifts.Add((0, 0)); continue; }
            var min = c0;
            double sum = 0;
            var count = 0;
            for (var y = 0; y <= 2 * my; y++)
                for (var x = 0; x <= 2 * mx; x++)
                {
                    if (double.IsNaN(costs[y, x])) continue;
                    sum += costs[y, x];
                    count++;
                    min = Math.Min(min, costs[y, x]);
                }
            // Among (near) ties take the smallest shift: vertical stripes look the same shifted up or down.
            var (by, bx, best) = (my, mx, c0);
            var bestSize = int.MaxValue;
            for (var y = 0; y <= 2 * my; y++)
                for (var x = 0; x <= 2 * mx; x++)
                {
                    if (double.IsNaN(costs[y, x]) || costs[y, x] > min * 1.03 + 1e-6) continue;
                    var size = Math.Abs(y - my) * 10 + Math.Abs(x - mx);
                    if (size < bestSize) (by, bx, best, bestSize) = (y, x, costs[y, x], size);
                }
            var others = count > 1 ? (sum - best) / (count - 1) : c0;
            gains.Add(Math.Min(1 - best / c0, 1 - best / Math.Max(others, 1e-9)));
            var sx = (double)(bx - mx);
            if (bx > 0 && bx < 2 * mx) sx += Refine(costs[by, bx - 1], best, costs[by, bx + 1]);
            var sy = (double)(by - my);
            if (by > 0 && by < 2 * my) sy += Refine(costs[by - 1, bx], best, costs[by + 1, bx]);
            shifts.Add((sx / dt, sy / dt));
        }
        if (pairs < 3) return default;
        var moving = shifts.Where(s => s.Dx != 0 || s.Dy != 0).ToList();
        if (moving.Count < 3) return new Move(0, 0, gains.Average(), 0);
        double Med(IEnumerable<double> v) { var l = v.OrderBy(x => x).ToList(); return l[l.Count / 2]; }
        var vx = Med(moving.Select(s => s.Dx));
        var vy = Med(moving.Select(s => s.Dy));
        var horizontal = Math.Abs(vx) / W >= Math.Abs(vy) / H;
        var agree = shifts.Count(s => horizontal ? Math.Sign(s.Dx) == Math.Sign(vx) && s.Dx != 0 : Math.Sign(s.Dy) == Math.Sign(vy) && s.Dy != 0)
                    / (double)shifts.Count;
        return new Move(vx, vy, gains.Average(), agree);
    }

    /// <summary>Sub-cell offset of a cost minimum from its two neighbours (parabola).</summary>
    private static double Refine(double left, double mid, double right)
    {
        if (double.IsNaN(left) || double.IsNaN(right)) return 0;
        var den = left - 2 * mid + right;
        return den > 1e-9 ? Math.Clamp(0.5 * (left - right) / den, -0.5, 0.5) : 0;
    }

    /// <summary>Mean squared colour difference between the central cells of a and the same cells of b shifted by (dx, dy).</summary>
    private static double GridCost(Rgb?[] a, Rgb?[] b, int dx, int dy, int mx, int my)
    {
        double sum = 0;
        var n = 0;
        for (var y = my; y < H - my; y++)
            for (var x = mx; x < W - mx; x++)
            {
                if (a[y * W + x] is not { } p || b[(y + dy) * W + x + dx] is not { } q) continue;
                sum += (p.R - q.R) * (p.R - q.R) + (p.G - q.G) * (p.G - q.G) + (p.B - q.B) * (p.B - q.B);
                n++;
            }
        return n < 10 ? double.NaN : sum / n;
    }

    /// <summary>
    /// Turning speed around the centre (turns per second, + = clockwise on the keyboard) from the circular shift of
    /// the colour-by-angle profile between frames ~0.5 s apart, with the same Gain / Agree as <see cref="Motion"/>.
    /// </summary>
    private (double W, double Gain, double Agree) Rotation(Frame[] frames)
    {
        Rgb?[] Profile(Frame f)
        {
            var sum = new Rgb[AngleBins];
            var w = new double[AngleBins];
            for (var k = 0; k < f.Keys.Length; k++)
            {
                if (f.Keys[k] is not { } c) continue;
                var (bin, weight) = _angle[k];
                sum[bin] = new Rgb(sum[bin].R + c.R * weight, sum[bin].G + c.G * weight, sum[bin].B + c.B * weight);
                w[bin] += weight;
            }
            return Enumerable.Range(0, AngleBins).Select(b => w[b] > 0 ? new Rgb(sum[b].R / w[b], sum[b].G / w[b], sum[b].B / w[b]) : (Rgb?)null).ToArray();
        }
        const int maxShift = 2;
        var speeds = new List<double>();
        var gains = new List<double>();
        for (var i = 0; i < frames.Length; i++)
        {
            var j = Array.FindIndex(frames, i + 1, f => f.T - frames[i].T >= 500);
            if (j < 0 || frames[j].T - frames[i].T > 800) continue;
            var a = Profile(frames[i]);
            var b = Profile(frames[j]);
            var costs = new double[2 * maxShift + 1];
            for (var s = -maxShift; s <= maxShift; s++)
            {
                double sum = 0;
                var n = 0;
                for (var k = 0; k < AngleBins; k++)
                    if (a[k] is { } p && b[((k + s) % AngleBins + AngleBins) % AngleBins] is { } q)
                    {
                        sum += (p.R - q.R) * (p.R - q.R) + (p.G - q.G) * (p.G - q.G) + (p.B - q.B) * (p.B - q.B);
                        n++;
                    }
                costs[s + maxShift] = n == 0 ? double.MaxValue : sum / n;
            }
            var c0 = costs[maxShift];
            if (c0 < 1e-4) { gains.Add(0); speeds.Add(0); continue; }
            var best = Array.IndexOf(costs, costs.Min());
            var others = costs.Where((_, k) => k != best).Average();
            gains.Add(Math.Min(1 - costs[best] / c0, 1 - costs[best] / others));
            var shift = best - maxShift + (best > 0 && best < costs.Length - 1 ? Refine(costs[best - 1], costs[best], costs[best + 1]) : 0);
            speeds.Add(shift / AngleBins / ((frames[j].T - frames[i].T) / 1000.0));
        }
        if (speeds.Count < 3) return (0, 0, 0);
        var moving = speeds.Where(s => s != 0).OrderBy(s => s).ToList();
        if (moving.Count < 3) return (0, gains.Average(), 0);
        var w = moving[moving.Count / 2];
        return (w, gains.Average(), speeds.Count(s => Math.Sign(s) == Math.Sign(w)) / (double)speeds.Count);
    }

    internal static (double H, double S, double V) Hsv(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double h = 0;
        if (d > 1e-9)
        {
            if (max == r) h = ((g - b) / d % 6 + 6) % 6;
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }
        return (h, max <= 0 ? 0 : d / max, max);
    }
}

/// <summary>
/// Turns the scene decisions into keyboard writes without wearing the flash: a new effect, direction or rainbow
/// choice is applied once it stayed the same for <see cref="StableMs"/>; a change of speed, brightness or colour only
/// alone needs <see cref="DetailStableMs"/> (a wave whose colour keeps cycling therefore keeps one colour), and
/// going back from a firmware effect to the live colour needs <see cref="ToLiveStableMs"/> (a colour-cycling wave
/// briefly matching its background looks like one colour for a few seconds). Never
/// sooner than <see cref="MinGapMs"/> after the previous write (<see cref="SlowGapMs"/> when the scene keeps
/// changing), and at most <see cref="HourlyWrites"/> per hour. Pure logic; the caller does the writes.
/// </summary>
public sealed class SceneMirror
{
    public const int StableMs = 2000, ToLiveStableMs = 10000, DetailStableMs = 20000, MinGapMs = 8000, SlowGapMs = 60000, HourlyWrites = 20;

    private readonly List<long> _writes = new();
    private string? _candidate;
    private long _candidateSince;

    /// <summary>The look the keyboard has now (null until the first write).</summary>
    public SceneLook? Applied { get; private set; }

    /// <summary>Starts over (owner switch, new connection) from the look the keyboard has.</summary>
    public void Reset(SceneLook? applied = null)
    {
        Applied = applied;
        _candidate = null;
    }

    /// <summary>The look to write now, or null to keep the current one.</summary>
    public SceneLook? Step(SceneLook? decided, long now)
    {
        if (decided is null) return null;
        if (Applied?.Signature == decided.Signature) { _candidate = null; return null; }
        if (_candidate != decided.Signature) { _candidate = decided.Signature; _candidateSince = now; return null; }
        var stable = Applied is not { } applied ? StableMs
            : applied.SameFamily(decided) ? DetailStableMs
            : decided.Live ? ToLiveStableMs
            : StableMs;
        if (now - _candidateSince < stable) return null;
        _writes.RemoveAll(t => now - t > 3_600_000);
        if (_writes.Count >= HourlyWrites) return null;
        var gap = _writes.Count(t => now - t <= 120_000) >= 4 ? SlowGapMs : MinGapMs;
        if (_writes.Count > 0 && now - _writes[^1] < gap) return null;
        return decided;
    }

    /// <summary>The caller wrote <paramref name="look"/> to the keyboard.</summary>
    public void Written(SceneLook look, long now)
    {
        Applied = look;
        _candidate = null;
        _writes.Add(now);
    }
}
