using System.Globalization;
using System.Text;
using SharkBabyDrivers.Core.Devices.X68;

namespace SharkBabyDrivers.Core.Lighting;

/// <summary>One key as the SignalRGB plugin exposes it: matrix slot, name and integer grid position.</summary>
public sealed record SignalRgbKey(int Slot, string Name, int X, int Y);

/// <summary>
/// The key map the SignalRGB plugin declares (LedNames/LedPositions). Built from the X68 V2 (id 2901) layout, which
/// is a superset of the original revision (it adds the F-row); slots a connected keyboard does not have are ignored.
/// Positions are on a 16 × 6 grid: x = centre of the key in key units, y = row.
/// </summary>
public static class SignalRgbKeyMap
{
    public const int GridWidth = 16, GridHeight = 6;

    public static IReadOnlyList<SignalRgbKey> Keys { get; } = Build(X68Model.Rev2901);

    public static IReadOnlyList<SignalRgbKey> Build(X68Model model)
    {
        var keys = new List<SignalRgbKey>();
        foreach (var c in model.Caps)
        {
            var name = c.Label switch
            {
                "⌫" => "Backspace", "↑" => "Up Arrow", "↓" => "Down Arrow", "←" => "Left Arrow", "→" => "Right Arrow",
                "Shift" or "Ctrl" or "Alt" or "Win" => (keys.Any(k => k.Name.EndsWith(c.Label, StringComparison.Ordinal)) ? "Right " : "Left ") + c.Label,
                _ => c.Label,
            };
            keys.Add(new SignalRgbKey(c.Slot, name,
                Math.Clamp((int)Math.Floor(c.X + c.Width / 2), 0, GridWidth - 1),
                Math.Clamp((int)Math.Floor(c.Y), 0, GridHeight - 1)));
        }
        return keys;
    }

    /// <summary>The JavaScript table embedded in the plugin (kept in sync by a test).</summary>
    public static string ToJavaScript()
    {
        var sb = new StringBuilder("const vKeys = [\n");
        foreach (var row in Keys.GroupBy(k => k.Y))
        {
            sb.Append('\t');
            sb.AppendJoin(" ", row.Select(k => string.Create(CultureInfo.InvariantCulture,
                $"[{k.Slot}, \"{k.Name.Replace("\\", "\\\\").Replace("\"", "\\\"")}\", {k.X}, {k.Y}],")));
            sb.Append('\n');
        }
        sb.Append("];");
        return sb.ToString();
    }
}
