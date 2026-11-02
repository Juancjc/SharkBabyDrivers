using SharkBabyDrivers.Core.Devices.X68;
using SharkBabyDrivers.Core.Lighting;

namespace SharkBabyDrivers.Tests;

/// <summary>Picking the firmware look that best imitates SignalRGB's per-key scene.</summary>
public class SignalRgbSceneTests
{
    private const int FrameMs = 100;

    /// <summary>Feeds frames at 10 Hz drawn by <paramref name="paint"/>(x, y, seconds) and decides at the end.</summary>
    private static SceneLook? Run(Func<int, int, double, (double R, double G, double B)> paint, int seconds = 3) =>
        RunAll(paint, seconds).LastOrDefault();

    /// <summary>Same, deciding four times a second like the app; returns every decision after the first 3 s.</summary>
    private static List<SceneLook?> RunAll(Func<int, int, double, (double R, double G, double B)> paint, int seconds)
    {
        var scene = new SignalRgbScene();
        var looks = new List<SceneLook?>();
        Details = new List<string>();
        long t = 10_000;
        for (var i = 0; i <= seconds * 1000 / FrameMs; i++, t += FrameMs)
        {
            var keys = SignalRgbKeyMap.Keys.ToDictionary(k => k.Slot, k =>
            {
                var c = paint(k.X, k.Y, (t - 10_000) / 1000.0);
                return ((byte)Math.Round(Math.Clamp(c.R, 0, 1) * 255), (byte)Math.Round(Math.Clamp(c.G, 0, 1) * 255), (byte)Math.Round(Math.Clamp(c.B, 0, 1) * 255));
            });
            scene.Add(new KeyColorsFrame(0x502F, keys, t));
            if (i % 2 == 0 && t - 10_000 >= 3000)
            {
                looks.Add(scene.Decide(t));
                Detail = scene.LastDetail + " => " + looks[^1];
                Details.Add(Detail);
            }
        }
        return looks;
    }

    [ThreadStatic] private static string? Detail;
    [ThreadStatic] private static List<string>? Details;

    /// <summary>Well-mixed pseudo-random number in [0, 1) (System.Random with nearby seeds gives similar values).</summary>
    private static double Hash01(int n)
    {
        unchecked
        {
            var z = (ulong)n * 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (z ^ (z >> 31)) / 18446744073709551616.0;
        }
    }

    private static (double, double, double) Hue(double turns)
    {
        var h = (turns % 1 + 1) % 1 * 6;
        var x = 1 - Math.Abs(h % 2 - 1);
        return (int)h switch { 0 => (1, x, 0), 1 => (x, 1, 0), 2 => (0, 1, x), 3 => (0, x, 1), 4 => (x, 0, 1), _ => (1, 0, x) };
    }

    [Fact]
    public void Rainbow_wave_moving_right_becomes_the_firmware_rainbow_wave()
    {
        // 4 columns per second = a quarter of the board per second.
        var look = Run((x, _, t) => Hue((x - 4 * t) / 16.0));
        Assert.True(look is not null, Detail);
        Assert.False(look!.Live);
        Assert.Equal(X68LightMode.Wave, look.Mode);
        Assert.Equal(0, look.Option);
        Assert.True(look.Rainbow);
        Assert.Equal(SignalRgbScene.SpeedLevel(0.25), look.Speed);
        Assert.Equal(4, look.Brightness);
    }

    [Fact]
    public void Single_colour_band_moving_left_becomes_a_wave_in_that_colour()
    {
        var look = Run((x, _, t) =>
        {
            var d = Math.Abs(((x + 6 * t) % 16 + 16) % 16 - 8);
            var k = Math.Max(0, 1 - d / 3.0);
            return (0.1 * k, 0.2 * k, 1.0 * k);
        });
        Assert.True(look is not null, Detail);
        Assert.Equal(X68LightMode.Wave, look!.Mode);
        Assert.Equal(1, look.Option);
        Assert.False(look.Rainbow);
        Assert.True((look.Color & 0xFF) > 200 && (look.Color >> 16) < 0x60, $"#{look.Color:X6} should be blue");
    }

    [Fact]
    public void Fast_wave_keeps_its_direction()
    {
        // 20 columns per second: faster than the slow measurement can follow.
        var look = Run((x, _, t) => Hue((x + 20 * t) / 16.0));
        Assert.True(look is { Mode: X68LightMode.Wave }, Detail);
        Assert.Equal(1, look!.Option);
        Assert.Equal(4, look.Speed);
    }

    [Fact]
    public void Background_with_a_colour_cycling_wave_passing_is_a_single_colour_wave_written_once()
    {
        // SignalRGB "Color Wave": red background, a band moving right whose colour cycles through the hues (~40 s).
        // The band covers about a quarter of the board, like the recorded effect (~20 % of the keys stand out).
        var looks = RunAll((x, _, t) =>
        {
            var k = Math.Clamp((Math.Cos(2 * Math.PI * (x - 12 * t) / 16.0) - 0.6) / 0.3, 0, 1);
            var (r, g, b) = Hue(t / 40.0 + 0.6);
            return (0.84 * (1 - k) + r * k, g * k, b * k);
        }, seconds: 60);
        var firstRainbow = looks.FindIndex(l => l is { Rainbow: true });
        Assert.True(firstRainbow < 0, firstRainbow < 0 ? "" : $"#{firstRainbow}: {Details![firstRainbow]}");
        // While the passing colour is close to the red background the board briefly looks like one colour.
        var waves = looks.Count(l => l is { Mode: X68LightMode.Wave, Option: 0, Rainbow: false });
        Assert.True(waves >= looks.Count * 0.75, $"{waves}/{looks.Count} {Detail}");

        // The colour keeps drifting, but the keyboard is written once (colour changes need 20 steady seconds).
        var mirror = new SceneMirror();
        mirror.Reset(SceneLook.LiveColor);
        var writes = 0;
        for (var i = 0; i < looks.Count; i++)
            if (mirror.Step(looks[i], i * 200L) is { } w) { mirror.Written(w, i * 200L); writes++; } // a decision every 200 ms
        var runs = new List<string>();
        foreach (var l in looks)
        {
            var k = l?.Kind ?? "-";
            if (runs.Count > 0 && runs[^1].StartsWith(k + "×")) runs[^1] = $"{k}×{int.Parse(runs[^1].Split('×')[1]) + 1}";
            else runs.Add($"{k}×1");
        }
        Assert.True(writes == 1, $"{writes} writes: {string.Join(" ", runs)}");
    }

    [Fact]
    public void Narrow_band_passing_over_a_background_that_cycles_through_the_hues_is_not_a_rainbow()
    {
        // What SignalRGB's "Color Wave" (colour cycle) really sends: ~80 % of the keys in one colour that slowly goes
        // around the colour circle, and a narrow band of another colour passing to the right.
        var looks = RunAll((x, _, t) =>
        {
            var band = Math.Abs(((x - 14 * t) % 16 + 16) % 16 - 8) < 1.5;
            return band ? Hue(t / 30.0 + 0.5) : Hue(t / 30.0);
        }, seconds: 60);
        Assert.DoesNotContain(looks, l => l is { Rainbow: true });
        Assert.True(looks.Skip(2).Count(l => l is { Mode: X68LightMode.Wave, Option: 0 }) >= (looks.Count - 2) * 0.9, Detail);
    }

    [Fact]
    public void Two_colour_stripes_moving_right_are_not_read_as_vertical()
    {
        // Vertical stripes look the same shifted up or down: the tie must not become an upward wave.
        var look = Run((x, _, t) => ((int)Math.Floor((x - 9 * t) / 4.0) % 2 == 0 ? (1.0, 0.0, 0.0) : (0.0, 0.0, 1.0)));
        Assert.True(look is { Mode: X68LightMode.Wave, Option: 0 }, Detail);
    }

    [Fact]
    public void Sparse_drops_falling_become_the_firmware_rain()
    {
        var look = Run((x, y, t) =>
        {
            // One drop per column, falling 4 rows per second, each column with its own start.
            var head = ((t * 4 + Hash01(x) * 6) % 8);
            var d = head - y;
            return d is >= 0 and < 1.5 ? (0.2, 0.6, 1.0) : (0.0, 0.0, 0.0);
        });
        Assert.True(look is { Mode: X68LightMode.RainDown }, Detail);
        Assert.False(look!.Rainbow);
    }

    [Fact]
    public void Several_colours_breathing_together_become_the_firmware_breath()
    {
        var look = Run((x, y, t) =>
        {
            var k = 0.5 - 0.5 * Math.Cos(2 * Math.PI * t / 2.5);
            var (r, g, b) = Hue((x / 8.0 + y / 3.0) / 4.0);
            return (r * k, g * k, b * k);
        });
        Assert.True(look is { Mode: X68LightMode.Breath, Rainbow: true }, Detail);
    }

    [Fact]
    public void Single_colour_wave_stays_single_colour_over_time()
    {
        var looks = RunAll((x, _, t) =>
        {
            var k = 0.5 + 0.5 * Math.Cos(2 * Math.PI * (x - 5 * t) / 16.0);
            return (k, 0.1 * k, 0.6 * k);
        }, seconds: 15);
        Assert.All(looks.Skip(4), l => Assert.True(l is { Mode: X68LightMode.Wave, Option: 0, Rainbow: false }, Detail));
        Assert.Single(looks.Skip(4).Select(l => l!.Signature).Distinct());
    }

    [Fact]
    public void Rainbow_moving_down_becomes_a_downward_wave()
    {
        var look = Run((_, y, t) => Hue((y - 2.5 * t) / 6.0));
        Assert.True(look is not null, Detail);
        Assert.Equal(X68LightMode.Wave, look!.Mode);
        Assert.Equal(2, look.Option);
    }

    [Fact]
    public void Breathing_or_cycling_single_colour_stays_on_the_exact_live_colour()
    {
        Assert.True(Run((_, _, t) => (0.5 + 0.5 * Math.Sin(t * 3), 0, 0))!.Live);
        Assert.True(Run((_, _, t) => Hue(t * 0.3))!.Live);
    }

    [Fact]
    public void Still_rainbow_becomes_a_very_slow_rainbow_wave_and_other_still_pictures_stay_live()
    {
        Assert.True(Run((x, _, _) => Hue(x / 16.0)) is { Mode: X68LightMode.Wave, Speed: 0, Rainbow: true }, Detail);
        Assert.True(Run((x, _, _) => x < 8 ? (1.0, 0.0, 0.0) : (0.0, 0.0, 1.0))!.Live, Detail);
    }

    [Fact]
    public void Sparse_fireflies_on_black_are_not_a_single_colour()
    {
        var look = Run((x, y, t) =>
        {
            var phase = (t / 2.0 + Hash01(x * 7 + y)) % 1;
            return Hash01(x * 13 + y * 3) < 0.15 ? (Math.Sin(Math.PI * phase), 0.1 * Math.Sin(Math.PI * phase), 0.0) : (0.0, 0.0, 0.0);
        });
        Assert.True(look is { Live: false, Mode: X68LightMode.Raindrop }, Detail);
    }

    [Fact]
    public void Random_sparkles_become_raindrops()
    {
        var look = Run((x, y, t) =>
        {
            // A different random set of keys every frame (no direction at all).
            return Hash01(x * 1000 + y * 100 + (int)Math.Round(t * 10)) < 0.12 ? Hue((x + y) / 7.0) : (0, 0, 0.02);
        });
        Assert.True(look is not null, Detail);
        Assert.True(look!.Mode == X68LightMode.Raindrop, Detail);
    }

    /// <summary>
    /// SignalRGB's own effects, recorded from the plugin (7–11 s each, app setting SBD_SCENE_RECORD) and replayed:
    /// after the first 3 s at least 70 % of the decisions must be one of the expected effect / direction / rainbow
    /// choices ("|" separates alternatives).
    /// </summary>
    [Theory]
    [InlineData("solid-color", "live")]
    [InlineData("color-cycle", "live")]
    [InlineData("lightning", "live")]
    [InlineData("rainbow", "Wave/0/rainbow")]
    [InlineData("rainbow-rise", "Wave/3/")]
    [InlineData("color-wave", "Wave/0/colour")]          // red background, colour-cycling wave going right
    [InlineData("custom-wave", "Wave/0/colour")]         // red and blue bands: one covers most keys, the other passes
    [InlineData("neon-sunset-wave", "Wave/1/rainbow")]   // direction: Left; pink, orange and purple
    [InlineData("rain", "RainDown/0/rainbow")]           // colour mode: Rainbow
    [InlineData("drizzle", "RainDown/0/colour")]         // a narrow range of warm hues
    [InlineData("cyber-rain", "RainDown/0/rainbow")]     // yellow and blue bars
    [InlineData("psychedelic-dream", "RainDown/0/rainbow|Raindrop/0/rainbow")] // cyan, violet, magenta over dark indigo
    [InlineData("fireflies", "Raindrop/0/colour")]
    [InlineData("quad-color-breath", "Breath/0/rainbow")]
    public void Recorded_SignalRgb_effects_get_the_expected_look(string fixture, string expectedKind)
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "SharkBabyDrivers.sln"))) root = Path.GetDirectoryName(root)!;
        var path = Path.Combine(root, "tests", "SharkBabyDrivers.Tests", "Fixtures", "SignalRgb", fixture + ".txt.gz");
        using var gz = new System.IO.Compression.GZipStream(File.OpenRead(path), System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var frames = new List<KeyColorsFrame>();
        while (reader.ReadLine() is { } line)
            if (SceneRecording.Parse(line) is { } f) frames.Add(f);
        Assert.True(frames.Count > 50, $"{fixture}: {frames.Count} frames");

        var start = frames[0].ReceivedAt;
        var decisions = SceneRecording.Replay(frames).Where(d => d.T - start >= 3000 && d.Look is not null).ToList();
        var accepted = expectedKind.Split('|');
        var hits = decisions.Count(d => accepted.Any(k => d.Look!.Kind.StartsWith(k, StringComparison.Ordinal)));
        var summary = string.Join(", ", decisions.GroupBy(d => d.Look!.Kind).Select(g => $"{g.Key}×{g.Count()}"));
        Assert.True(hits >= decisions.Count * 0.7, $"{fixture}: {hits}/{decisions.Count} {expectedKind} ({summary})");
    }

    [Fact]
    public void Too_little_data_decides_nothing()
    {
        var scene = new SignalRgbScene();
        scene.Add(new KeyColorsFrame(0x502F, new Dictionary<int, (byte, byte, byte)> { [1] = (255, 0, 0) }, 1000));
        Assert.Null(scene.Decide(1000));
    }

    [Fact]
    public void Look_bytes_keep_the_other_fields_and_encode_the_effect()
    {
        var current = new byte[] { 0x87, 21, 2, 3, 0, 1, 2, 3 };
        var wave = new SceneLook(false, X68LightMode.Wave, 3, 4, 2, true, 0);
        var led = wave.LedBytes(current);
        Assert.Equal(0x87, led[0]);
        Assert.Equal((byte)X68LightMode.Wave, led[1]);
        Assert.Equal(0, led[2]);        // speed 4 = raw 0
        Assert.Equal(2, led[3]);
        Assert.Equal(3 << 4 | X68Lighting.Rainbow, led[4]);
        Assert.True(X68Lighting.IsPlausibleEffect(led));

        var red = new SceneLook(false, X68LightMode.Wave, 0, 2, 4, false, 0xFF2000).LedBytes(current);
        Assert.Equal(X68Lighting.CustomColor, red[4] & 0x0F);
        Assert.Equal(new byte[] { 0xFF, 0x20, 0x00 }, red[5..8]);

        // From a keyboard already in a rainbow wave, a single-colour wave must turn the rainbow off.
        var fromRainbow = new SceneLook(false, X68LightMode.Wave, 0, 4, 4, false, 0xFF00C6).LedBytes(led);
        Assert.Equal(X68Lighting.CustomColor, fromRainbow[4] & 0x0F);
        Assert.Equal(new byte[] { 0xFF, 0x00, 0xC6 }, fromRainbow[5..8]);
        Assert.False(SceneLook.FromLed(fromRainbow)!.Rainbow);
    }

    [Fact]
    public void Imitating_effect_read_back_from_the_keyboard_has_the_same_signature()
    {
        var current = new byte[] { 0x87, 21, 2, 3, 0, 1, 2, 3 };
        foreach (var look in new[]
                 {
                     new SceneLook(false, X68LightMode.Wave, 2, 3, 4, true, 0),
                     new SceneLook(false, X68LightMode.Raindrop, 0, 1, 4, false, 0xFF7800),
                 })
            Assert.Equal(look.Signature, SceneLook.FromLed(look.LedBytes(current))!.Signature);
        Assert.Null(SceneLook.FromLed(current));                                   // screen colour
        Assert.Null(SceneLook.FromLed([0x87, 1, 0, 4, 7, 255, 0, 0]));              // the user's own static colour
        Assert.Null(SceneLook.FromLed([0x87, 4, 0xBF, 4, 0xFB, 0xFF, 0xFF, 0xFF])); // garbled answer
    }

    [Fact]
    public void Mirror_waits_for_a_stable_scene_and_spaces_the_flash_writes()
    {
        var mirror = new SceneMirror();
        mirror.Reset(SceneLook.LiveColor);
        var wave = new SceneLook(false, X68LightMode.Wave, 0, 2, 4, true, 0);
        var drops = new SceneLook(false, X68LightMode.Raindrop, 0, 2, 4, true, 0);

        Assert.Null(mirror.Step(SceneLook.LiveColor, 0));        // already applied
        Assert.Null(mirror.Step(wave, 0));                      // new candidate
        Assert.Null(mirror.Step(wave, 1500));                   // not stable yet
        Assert.Same(wave, mirror.Step(wave, 2100));
        mirror.Written(wave, 2100);
        Assert.Null(mirror.Step(wave, 2500));                   // nothing to do

        Assert.Null(mirror.Step(drops, 3000));
        Assert.Null(mirror.Step(drops, 5500));                  // stable, but too soon after the last write
        Assert.Same(drops, mirror.Step(drops, 2100 + SceneMirror.MinGapMs));

        mirror.Written(drops, 2100 + SceneMirror.MinGapMs);

        // A scene that keeps changing (4 writes in 2 minutes) slows the writes down to one a minute.
        long t = 20_000;
        foreach (var look in new[] { wave, drops })
        {
            mirror.Step(look, t);
            t += SceneMirror.MinGapMs;
            Assert.NotNull(mirror.Step(look, t));
            mirror.Written(look, t);
        }
        mirror.Step(wave, t + 100);
        Assert.Null(mirror.Step(wave, t + SceneMirror.MinGapMs));
        Assert.NotNull(mirror.Step(wave, t + SceneMirror.SlowGapMs));
    }

    [Fact]
    public void Colour_speed_changes_and_going_back_to_live_need_longer_steadiness()
    {
        var mirror = new SceneMirror();
        var blue = new SceneLook(false, X68LightMode.Wave, 0, 2, 4, false, 0x0000FF);
        var green = blue with { Color = 0x00FF00 };
        var faster = blue with { Speed = 4 };
        mirror.Written(blue, 0);

        long t = 60_000;
        mirror.Step(green, t);
        Assert.Null(mirror.Step(green, t + SceneMirror.StableMs));                  // only the colour changed
        Assert.Same(green, mirror.Step(green, t + SceneMirror.DetailStableMs));
        mirror.Written(green, t += SceneMirror.DetailStableMs);

        t += 60_000;
        mirror.Step(faster, t);
        Assert.Null(mirror.Step(faster, t + 5000));                                 // only the speed changed
        mirror.Step(SceneLook.LiveColor, t + 6000);
        Assert.Null(mirror.Step(SceneLook.LiveColor, t + 6000 + SceneMirror.StableMs));
        Assert.Same(SceneLook.LiveColor, mirror.Step(SceneLook.LiveColor, t + 6000 + SceneMirror.ToLiveStableMs));
    }

    [Fact]
    public void Raindrops_and_rain_falling_are_one_family_for_the_flash()
    {
        var mirror = new SceneMirror();
        var down = new SceneLook(false, X68LightMode.RainDown, 0, 3, 4, true, 0);
        var drops = new SceneLook(false, X68LightMode.Raindrop, 0, 4, 4, true, 0);
        mirror.Written(down, 0);
        long t = 60_000;
        mirror.Step(drops, t);
        Assert.Null(mirror.Step(drops, t + SceneMirror.StableMs));
        Assert.Same(drops, mirror.Step(drops, t + SceneMirror.DetailStableMs));
        Assert.False(down.SameFamily(down with { Rainbow = false, Color = 0xFF0000 }));
    }

    [Fact]
    public void Mirror_writes_at_most_twenty_times_an_hour()
    {
        var mirror = new SceneMirror();
        mirror.Reset(SceneLook.LiveColor);
        var wave = new SceneLook(false, X68LightMode.Wave, 0, 2, 4, true, 0);
        long t = 0;
        var writes = 0;
        // A scene flipping between the wave and one colour every 70 s, for two hours.
        for (var i = 0; i < 2 * 3600 / 70; i++)
        {
            var look = i % 2 == 0 ? wave : SceneLook.LiveColor;
            for (var s = 0; s < 70_000; s += 250, t += 250)
                if (mirror.Step(look, t) is { } w) { mirror.Written(w, t); writes++; }
        }
        Assert.InRange(writes, 30, 2 * SceneMirror.HourlyWrites);
    }
}
