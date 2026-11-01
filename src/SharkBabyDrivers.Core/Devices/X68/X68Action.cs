using SharkBabyDrivers.Core.Input;

namespace SharkBabyDrivers.Core.Devices.X68;

public enum X68ActionKind : byte
{
    Key = 0x00,          // [0, modifiers, usage, usage2]; all zero = disabled
    Mouse = 0x01,        // [1, 0, button, arg]
    System = 0x02,       // [2, 0, code, 0]
    Consumer = 0x03,     // [3, 0, usageLo, usageHi]
    Profile = 0x08,      // [8, 0, op, arg]
    Macro = 0x09,        // [9, mode, index, 0]
    Function = 0x0A,     // firmware function (Fn, layer keys, knobs…)
    Fire = 0x0B,
    Led = 0x0D,
    Wireless = 0x0E,
}

[Flags]
public enum KeyMods : byte
{
    None = 0, LCtrl = 0x01, LShift = 0x02, LAlt = 0x04, LWin = 0x08, RCtrl = 0x10, RShift = 0x20, RAlt = 0x40, RWin = 0x80,
}

public enum X68MacroMode : byte { RepeatCount = 0, Toggle = 1, WhileHeld = 2 }

/// <summary>4-byte key action of the RY5088 matrix.</summary>
public readonly record struct X68Action(byte B0, byte B1, byte B2, byte B3)
{
    public static readonly X68Action Disabled = new(0, 0, 0, 0);

    public X68ActionKind Kind => (X68ActionKind)B0;
    public bool IsDisabled => B0 == 0 && B1 == 0 && B2 == 0 && B3 == 0;

    public static X68Action Key(byte usage, KeyMods mods = KeyMods.None, byte usage2 = 0) => new(0, (byte)mods, usage, usage2);
    public static X68Action MouseButton(byte button) => new(1, 0, button, 0);
    public static X68Action Consumer(ushort usage) => new(3, 0, (byte)usage, (byte)(usage >> 8));
    public static X68Action System(byte code) => new(2, 0, code, 0);
    public static X68Action Macro(int index, X68MacroMode mode) => new(9, (byte)mode, (byte)index, 0);
    public static X68Action ProfileSelect(int profile) => new(8, 0, 4, (byte)profile);
    public static X68Action ProfileNext => new(8, 0, 1, 0);
    public static X68Action ProfilePrev => new(8, 0, 2, 0);
    public static X68Action ProfileLoop => new(8, 0, 3, 0);
    public static X68Action Fn => new(0x0A, 1, 0, 0);

    public static X68Action Read(ReadOnlySpan<byte> matrix, int slot) =>
        new(matrix[slot * 4], matrix[slot * 4 + 1], matrix[slot * 4 + 2], matrix[slot * 4 + 3]);

    public void Write(Span<byte> matrix, int slot)
    {
        matrix[slot * 4] = B0;
        matrix[slot * 4 + 1] = B1;
        matrix[slot * 4 + 2] = B2;
        matrix[slot * 4 + 3] = B3;
    }

    /// <summary>The plain HID usage if this is a simple key without modifiers.</summary>
    public byte? PlainKey => B0 == 0 && B1 == 0 && B3 == 0 && B2 != 0 ? B2 : null;

    /// <summary>Short neutral description (UI layers translate the parts they know).</summary>
    public string Describe()
    {
        if (IsDisabled) return "—";
        switch (Kind)
        {
            case X68ActionKind.Key:
            {
                var parts = new List<string>();
                var m = (KeyMods)B1;
                if (m.HasFlag(KeyMods.LCtrl) || m.HasFlag(KeyMods.RCtrl)) parts.Add("Ctrl");
                if (m.HasFlag(KeyMods.LShift) || m.HasFlag(KeyMods.RShift)) parts.Add("Shift");
                if (m.HasFlag(KeyMods.LAlt) || m.HasFlag(KeyMods.RAlt)) parts.Add("Alt");
                if (m.HasFlag(KeyMods.LWin) || m.HasFlag(KeyMods.RWin)) parts.Add("Win");
                if (B2 != 0) parts.Add(HidUsage.KeyName(B2));
                if (B3 != 0) parts.Add(HidUsage.KeyName(B3));
                return string.Join(" + ", parts);
            }
            case X68ActionKind.Mouse:
                return B2 switch
                {
                    0xF0 => "Mouse L", 0xF1 => "Mouse R", 0xF2 => "Mouse M", 0xF3 => "Mouse ⟵", 0xF4 => "Mouse ⟶",
                    0xF5 => (sbyte)B3 > 0 ? "Wheel ↑" : "Wheel ↓",
                    _ => $"Mouse 0x{B2:X2}",
                };
            case X68ActionKind.Consumer:
            {
                var u = (ushort)(B2 | B3 << 8);
                return HidUsage.ConsumerNames.TryGetValue(u, out var n) ? n : $"Media 0x{u:X4}";
            }
            case X68ActionKind.System: return B2 switch { 0x81 => "Power", 0x82 => "Sleep", 0x83 => "Wake", _ => $"Sys 0x{B2:X2}" };
            case X68ActionKind.Macro: return $"Macro {B2 + 1}";
            case X68ActionKind.Profile: return B2 switch { 0 or 4 => $"Perfil {B3 + 1}", 1 => "Perfil +", 2 => "Perfil −", 3 => "Perfil ↻", _ => "Perfil" };
            case X68ActionKind.Function when B1 == 1: return "Fn";
            default: return $"{B0:X2} {B1:X2} {B2:X2} {B3:X2}";
        }
    }

    public override string ToString() => $"{B0:X2}{B1:X2}{B2:X2}{B3:X2}";
}

/// <summary>Keyboard macro: u16 repeat count, then (code, flags[, delayLo, delayHi]) events, max 256 bytes.</summary>
public static class X68Macro
{
    public const int MaxBytes = 256;
    public const byte MouseLeft = 0xF0, MouseRight = 0xF1, MouseMiddle = 0xF2, MouseBack = 0xF3, MouseForward = 0xF4;

    public sealed record Step(byte Code, bool Down, int DelayMs);

    public static byte[] Encode(IReadOnlyList<Step> steps, int repeat)
    {
        var buf = new List<byte> { (byte)repeat, (byte)(repeat >> 8) };
        foreach (var s in steps)
        {
            var delay = Math.Clamp(s.DelayMs, 1, 65535); // 0 would be decoded as a long-form delay by the firmware
            buf.Add(s.Code);
            if (delay <= 127) buf.Add((byte)((s.Down ? 0x80 : 0) | delay));
            else
            {
                buf.Add((byte)(s.Down ? 0x80 : 0));
                buf.Add((byte)delay);
                buf.Add((byte)(delay >> 8));
            }
        }
        if (buf.Count > MaxBytes - 4) throw new ArgumentException("Macro longa demais para o teclado (máx. ~250 bytes).");
        return buf.ToArray();
    }

    public static (List<Step> Steps, int Repeat) Decode(ReadOnlySpan<byte> raw)
    {
        var steps = new List<Step>();
        if (raw.Length < 2) return (steps, 1);
        var repeat = raw[0] | raw[1] << 8;
        var i = 2;
        while (i + 1 < raw.Length)
        {
            var code = raw[i];
            var flags = raw[i + 1];
            if (code == 0 && flags == 0) break;
            if (code == 0xF9)
            {
                // mouse move: skip (not produced by this app); keep stream aligned
                i += flags != 0 ? 4 : 6;
                continue;
            }
            var down = (flags & 0x80) != 0;
            int delay;
            if ((flags & 0x7F) != 0)
            {
                delay = flags & 0x7F;
                i += 2;
            }
            else
            {
                if (i + 3 >= raw.Length) break;
                delay = raw[i + 2] | raw[i + 3] << 8;
                i += 4;
            }
            steps.Add(new Step(code, down, delay));
        }
        return (steps, Math.Max(repeat, 1));
    }
}
