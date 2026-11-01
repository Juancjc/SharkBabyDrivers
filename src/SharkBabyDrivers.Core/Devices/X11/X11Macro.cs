namespace SharkBabyDrivers.Core.Devices.X11;

public enum MacroPlayMode : byte
{
    /// <summary>Plays the sequence N times.</summary>
    RepeatCount = 0x00,
    /// <summary>Repeats until any key is pressed.</summary>
    UntilKeyPressed = 0x01,
    /// <summary>Repeats while the button is held.</summary>
    WhileHeld = 0x02,
}

/// <summary>One step of a macro: a key (HID usage, or mouse button 0xF0-0xF4) pressed or released, then a pause.</summary>
public sealed record MacroStep(byte Key, bool Pressed, int DelayMs);

public sealed record MacroDefinition(string Name, IReadOnlyList<MacroStep> Steps, MacroPlayMode Mode = MacroPlayMode.RepeatCount, int Repeat = 1);

/// <summary>
/// Macro encoding of the official X11 software (CMS_4_Page @0x4A2430):
/// a 131-byte logical report, split on the wire into three 64-byte pages.
/// </summary>
public static class X11Macro
{
    public const int LogicalLength = 0x83;
    public const int PageLength = 0x40;
    public const int PagePayload = 60;
    public const int MaxPairs = 50; // bytes 29..128

    /// <summary>Mouse buttons inside macros use these key codes (matches the official recorder).</summary>
    public const byte MouseLeft = 0xF1, MouseRight = 0xF2, MouseMiddle = 0xF3, MouseBack = 0xF4, MouseForward = 0xF5;

    /// <remarks>
    /// Header: 09 83 slot mode 00 00 00 repeat. The official writer copies the play mode (selector index 0-2)
    /// to byte 3 and the repeat count (1-255, 0 coerced to 1) to byte 7; OpenSharkX11 uses the same offsets.
    /// </remarks>
    public static byte[] BuildLogical(int slotId, MacroDefinition macro)
    {
        var r = new byte[LogicalLength];
        r[0] = X11Protocol.RidMacro;
        r[1] = LogicalLength;
        r[2] = (byte)slotId;
        r[3] = (byte)macro.Mode;
        r[7] = (byte)Math.Clamp(macro.Repeat, 1, 255);

        var pairs = new List<(byte A, byte B)>();
        foreach (var step in macro.Steps)
        {
            var flag = step.Pressed ? 0x01 : 0x81;
            var delay = Math.Max(step.DelayMs, 1);
            if (delay > 1270)
            {
                var hundredsOfTwo = delay / 200;
                var rest = Math.Max(delay - hundredsOfTwo * 200, 1);
                pairs.Add(((byte)(DelayUnits(rest) | flag), step.Key));
                pairs.Add(((byte)Math.Min(hundredsOfTwo, 255), 0x03));
            }
            else
            {
                pairs.Add(((byte)(DelayUnits(delay) | flag), step.Key));
            }
        }
        if (pairs.Count > MaxPairs)
            throw new ArgumentException($"Macro longa demais: {pairs.Count} pares (máximo {MaxPairs}).");
        r[28] = (byte)pairs.Count;
        for (var i = 0; i < pairs.Count; i++)
        {
            r[29 + i * 2] = pairs[i].A;
            r[30 + i * 2] = pairs[i].B;
        }
        var sum = X11Protocol.Sum16(r.AsSpan(3, 0x81 - 3));
        r[0x81] = (byte)(sum >> 8);
        r[0x82] = (byte)sum;
        return r;
    }

    private static int DelayUnits(int ms) => (int)(ms / 10.0 + 0.5);

    /// <summary>Splits the logical report into the three feature-report pages: 09 40 slot page + 60 bytes.</summary>
    public static IReadOnlyList<byte[]> Paginate(byte[] logical)
    {
        var pages = new List<byte[]>();
        for (var page = 0; page < 3; page++)
        {
            var p = new byte[PageLength];
            p[0] = X11Protocol.RidMacro;
            p[1] = PageLength;
            p[2] = logical[2];
            p[3] = (byte)page;
            var from = 3 + page * PagePayload;
            var count = Math.Min(PagePayload, logical.Length - from);
            if (count > 0) Array.Copy(logical, from, p, 4, count);
            pages.Add(p);
        }
        return pages;
    }
}
