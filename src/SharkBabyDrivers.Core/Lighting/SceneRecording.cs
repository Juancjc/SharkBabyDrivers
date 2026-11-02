using System.Globalization;
using System.Text;

namespace SharkBabyDrivers.Core.Lighting;

/// <summary>
/// Text form of SignalRGB's per-key snapshots, one per line: "time pid slot:RRGGBB slot:RRGGBB…". The app writes it
/// when the environment variable <see cref="EnvironmentVariable"/> names a file (diagnostics), the CLI replays it
/// through <see cref="SignalRgbScene"/> (scene-replay) and the tests use recorded scenes as fixtures.
/// </summary>
public static class SceneRecording
{
    public const string EnvironmentVariable = "SBD_SCENE_RECORD";

    public static string Format(KeyColorsFrame frame)
    {
        var sb = new StringBuilder();
        sb.Append(frame.ReceivedAt.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(frame.ProductId.ToString("X4"));
        foreach (var (slot, c) in frame.Keys.OrderBy(k => k.Key))
            sb.Append(' ').Append(slot.ToString(CultureInfo.InvariantCulture)).Append(':').Append($"{c.R:X2}{c.G:X2}{c.B:X2}");
        return sb.ToString();
    }

    public static KeyColorsFrame? Parse(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t)
            || !ushort.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var pid)) return null;
        var keys = new Dictionary<int, (byte R, byte G, byte B)>();
        foreach (var p in parts.Skip(2))
        {
            var colon = p.IndexOf(':');
            if (colon < 1 || p.Length - colon - 1 != 6 || !int.TryParse(p[..colon], NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot)
                || !uint.TryParse(p[(colon + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return null;
            keys[slot] = ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
        return new KeyColorsFrame(pid, keys, t);
    }

    /// <summary>Reads a recording, also while the app is still appending to it.</summary>
    public static IEnumerable<KeyColorsFrame> Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            if (Parse(line) is { } frame) yield return frame;
    }

    /// <summary>
    /// Replays frames through a fresh <see cref="SignalRgbScene"/>, deciding every 250 ms of recorded time as the
    /// app does. Returns (time, decided look, scene numbers) for every decision.
    /// </summary>
    public static List<(long T, SceneLook? Look, string Detail)> Replay(IEnumerable<KeyColorsFrame> frames)
    {
        var scene = new SignalRgbScene();
        var result = new List<(long, SceneLook?, string)>();
        long next = long.MinValue;
        foreach (var f in frames)
        {
            scene.Add(f);
            if (next == long.MinValue) next = f.ReceivedAt + 250;
            while (f.ReceivedAt >= next)
            {
                var look = scene.Decide(next);
                result.Add((next, look, scene.LastDetail));
                next += 250;
            }
        }
        return result;
    }
}
