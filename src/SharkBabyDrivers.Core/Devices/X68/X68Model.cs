using SharkBabyDrivers.Core.Input;

namespace SharkBabyDrivers.Core.Devices.X68;

/// <summary>One physical key cap for drawing the keyboard: position in key units and its matrix slot.</summary>
public sealed record KeyCap(int Slot, string Label, double X, double Y, double Width, byte DefaultUsage);

/// <summary>Model data (default matrices + physical layout) for the two X68 PRO HE hardware revisions.</summary>
public sealed class X68Model
{
    public uint Id { get; }
    public string Name { get; }
    public byte[] DefaultKeys { get; }
    public byte[] DefaultFnWin { get; }
    public byte[] DefaultFnMac { get; }
    public IReadOnlyList<KeyCap> Caps { get; }
    public double Width => Caps.Max(c => c.X + c.Width);
    public double Height => Caps.Max(c => c.Y) + 1;

    private X68Model(uint id, string name, byte[] keys, byte[] fnWin, byte[] fnMac, bool fRow)
    {
        Id = id; Name = name; DefaultKeys = keys; DefaultFnWin = fnWin; DefaultFnMac = fnMac;
        Caps = BuildLayout(keys, fRow);
    }

    public static readonly X68Model Rev2370 = new(2370, "X68 PRO HE", X68DefaultMatrices.Id2370Keys,
        X68DefaultMatrices.Id2370FnWin, X68DefaultMatrices.Id2370FnMac, fRow: false);
    public static readonly X68Model Rev2901 = new(2901, "X68 PRO HE (v2)", X68DefaultMatrices.Id2901Keys,
        X68DefaultMatrices.Id2901FnWin, X68DefaultMatrices.Id2901FnMac, fRow: true);

    public static X68Model ForId(uint id) => id == 2901 ? Rev2901 : Rev2370;
    public static bool IsKnown(uint id) => id is 2370 or 2901;

    /// <summary>Slot whose factory action is the given plain key (or Fn).</summary>
    public int? SlotOf(byte usage)
    {
        for (var s = 0; s < X68Protocol.Slots; s++)
            if (X68Action.Read(DefaultKeys, s).PlainKey == usage) return s;
        return null;
    }

    public int? FnSlot()
    {
        for (var s = 0; s < X68Protocol.Slots; s++)
            if (X68Action.Read(DefaultKeys, s) == X68Action.Fn) return s;
        return null;
    }

    public X68Action DefaultAction(int slot) => X68Action.Read(DefaultKeys, slot);

    // ANSI 68-key (65%) layout; widths in key units. 0xFF = Fn, 0 = gap.
    // A property (not a field): Rev2370/Rev2901 above are built during static init, before later fields exist.
    private static (byte Usage, double Width)[][] Rows =>
    [
        [(0x29, 1), (0x1E, 1), (0x1F, 1), (0x20, 1), (0x21, 1), (0x22, 1), (0x23, 1), (0x24, 1), (0x25, 1), (0x26, 1), (0x27, 1), (0x2D, 1), (0x2E, 1), (0x2A, 2), (0x4C, 1)],
        [(0x2B, 1.5), (0x14, 1), (0x1A, 1), (0x08, 1), (0x15, 1), (0x17, 1), (0x1C, 1), (0x18, 1), (0x0C, 1), (0x12, 1), (0x13, 1), (0x2F, 1), (0x30, 1), (0x31, 1.5), (0x4B, 1)],
        [(0x39, 1.75), (0x04, 1), (0x16, 1), (0x07, 1), (0x09, 1), (0x0A, 1), (0x0B, 1), (0x0D, 1), (0x0E, 1), (0x0F, 1), (0x33, 1), (0x34, 1), (0x28, 2.25), (0x4E, 1)],
        [(0xE1, 2.25), (0x1D, 1), (0x1B, 1), (0x06, 1), (0x19, 1), (0x05, 1), (0x11, 1), (0x10, 1), (0x36, 1), (0x37, 1), (0x38, 1), (0xE5, 1.75), (0x52, 1)],
        [(0xE0, 1.25), (0xE3, 1.25), (0xE2, 1.25), (0x2C, 6.25), (0xFF, 1), (0xE4, 1), (0, 1), (0x50, 1), (0x51, 1), (0x4F, 1)],
    ];

    private static IReadOnlyList<KeyCap> BuildLayout(byte[] keys, bool fRow)
    {
        var caps = new List<KeyCap>();
        int? Find(byte usage)
        {
            for (var s = 0; s < X68Protocol.Slots; s++)
            {
                var a = X68Action.Read(keys, s);
                if (usage == 0xFF ? a == X68Action.Fn : a.PlainKey == usage) return s;
            }
            return null;
        }
        var y = 0.0;
        if (fRow)
        {
            var x = 1.25;
            for (var f = 0; f < 12; f++)
            {
                var usage = (byte)(0x3A + f);
                if (Find(usage) is { } slot) caps.Add(new KeyCap(slot, HidUsage.KeyName(usage), x, y, 1, usage));
                x += f % 4 == 3 ? 1.25 : 1;
            }
            y += 1.25;
        }
        foreach (var row in Rows)
        {
            var x = 0.0;
            foreach (var (usage, width) in row)
            {
                if (usage != 0 && Find(usage) is { } slot)
                {
                    var label = usage == 0xFF ? "Fn" : usage switch
                    {
                        0xE0 => "Ctrl", 0xE1 => "Shift", 0xE2 => "Alt", 0xE3 => "Win", 0xE4 => "Ctrl", 0xE5 => "Shift",
                        0x4B => "PgUp", 0x4E => "PgDn", 0x4C => "Del", 0x2A => "⌫", 0x28 => "Enter", 0x39 => "Caps",
                        _ => HidUsage.KeyName(usage),
                    };
                    caps.Add(new KeyCap(slot, label, x, y, width, usage == 0xFF ? (byte)0 : usage));
                }
                x += width;
            }
            y += 1;
        }
        return caps;
    }
}
