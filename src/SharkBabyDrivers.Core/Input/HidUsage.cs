namespace SharkBabyDrivers.Core.Input;

/// <summary>HID keyboard (page 0x07) and consumer (page 0x0C) usage names, plus Windows virtual-key mapping for recording.</summary>
public static class HidUsage
{
    public static readonly IReadOnlyDictionary<byte, string> KeyNames = BuildKeyNames();

    private static Dictionary<byte, string> BuildKeyNames()
    {
        var d = new Dictionary<byte, string>();
        for (var i = 0; i < 26; i++) d[(byte)(0x04 + i)] = ((char)('A' + i)).ToString();
        string[] digits = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"];
        for (var i = 0; i < 10; i++) d[(byte)(0x1E + i)] = digits[i];
        var rest = new (byte, string)[]
        {
            (0x28, "Enter"), (0x29, "Esc"), (0x2A, "Backspace"), (0x2B, "Tab"), (0x2C, "Space"),
            (0x2D, "-"), (0x2E, "="), (0x2F, "["), (0x30, "]"), (0x31, "\\"), (0x32, "#"), (0x33, ";"),
            (0x34, "'"), (0x35, "`"), (0x36, ","), (0x37, "."), (0x38, "/"), (0x39, "Caps Lock"),
            (0x46, "Print Screen"), (0x47, "Scroll Lock"), (0x48, "Pause"), (0x49, "Insert"), (0x4A, "Home"),
            (0x4B, "Page Up"), (0x4C, "Delete"), (0x4D, "End"), (0x4E, "Page Down"), (0x4F, "→"), (0x50, "←"),
            (0x51, "↓"), (0x52, "↑"), (0x53, "Num Lock"), (0x54, "Num /"), (0x55, "Num *"), (0x56, "Num -"),
            (0x57, "Num +"), (0x58, "Num Enter"), (0x59, "Num 1"), (0x5A, "Num 2"), (0x5B, "Num 3"), (0x5C, "Num 4"),
            (0x5D, "Num 5"), (0x5E, "Num 6"), (0x5F, "Num 7"), (0x60, "Num 8"), (0x61, "Num 9"), (0x62, "Num 0"),
            (0x63, "Num ."), (0x64, "\\ (ISO)"), (0x65, "Menu"),
            (0xE0, "Ctrl"), (0xE1, "Shift"), (0xE2, "Alt"), (0xE3, "Win"),
            (0xE4, "Ctrl dir."), (0xE5, "Shift dir."), (0xE6, "Alt Gr"), (0xE7, "Win dir."),
        };
        foreach (var (k, v) in rest) d[k] = v;
        for (var i = 0; i < 12; i++) d[(byte)(0x3A + i)] = $"F{i + 1}";
        for (var i = 0; i < 12; i++) d[(byte)(0x68 + i)] = $"F{i + 13}";
        return d;
    }

    public static string KeyName(byte usage) => KeyNames.TryGetValue(usage, out var n) ? n : $"0x{usage:X2}";

    public static readonly IReadOnlyDictionary<ushort, string> ConsumerNames = new Dictionary<ushort, string>
    {
        [0x00E9] = "Volume +", [0x00EA] = "Volume −", [0x00E2] = "Mudo", [0x00CD] = "Play/Pause", [0x00B5] = "Próxima",
        [0x00B6] = "Anterior", [0x00B7] = "Parar", [0x0183] = "Player", [0x0192] = "Calculadora", [0x018A] = "E-mail",
        [0x0194] = "Este PC", [0x0221] = "Pesquisar", [0x0223] = "Navegador: início", [0x0224] = "Navegador: voltar",
        [0x0225] = "Navegador: avançar", [0x0227] = "Navegador: atualizar", [0x006F] = "Brilho tela +", [0x0070] = "Brilho tela −",
    };

    /// <summary>Windows virtual-key → HID usage (for recording macros and capturing shortcuts).</summary>
    public static byte? FromVirtualKey(int vk, bool extended = false)
    {
        if (vk is >= 0x41 and <= 0x5A) return (byte)(0x04 + vk - 0x41);
        if (vk is >= 0x31 and <= 0x39) return (byte)(0x1E + vk - 0x31);
        if (vk == 0x30) return 0x27;
        if (vk is >= 0x70 and <= 0x7B) return (byte)(0x3A + vk - 0x70);
        if (vk is >= 0x7C and <= 0x87) return (byte)(0x68 + vk - 0x7C);
        if (vk is >= 0x60 and <= 0x69) return vk == 0x60 ? (byte)0x62 : (byte)(0x59 + vk - 0x61);
        return vk switch
        {
            0x0D => extended ? (byte)0x58 : (byte)0x28,
            0x1B => 0x29, 0x08 => 0x2A, 0x09 => 0x2B, 0x20 => 0x2C,
            0xBD => 0x2D, 0xBB => 0x2E, 0xDB => 0x2F, 0xDD => 0x30, 0xDC => 0x31, 0xBA => 0x33, 0xDE => 0x34,
            0xC0 => 0x35, 0xBC => 0x36, 0xBE => 0x37, 0xBF => 0x38, 0xC1 => 0x38, 0xE2 => 0x64, 0x14 => 0x39,
            0x2C => 0x46, 0x91 => 0x47, 0x13 => 0x48, 0x2D => 0x49, 0x24 => 0x4A, 0x21 => 0x4B, 0x2E => 0x4C,
            0x23 => 0x4D, 0x22 => 0x4E, 0x27 => 0x4F, 0x25 => 0x50, 0x28 => 0x51, 0x26 => 0x52, 0x90 => 0x53,
            0x6F => 0x54, 0x6A => 0x55, 0x6D => 0x56, 0x6B => 0x57, 0x6E => 0x63, 0x5D => 0x65,
            0xA2 => 0xE0, 0xA0 => 0xE1, 0xA4 => 0xE2, 0x5B => 0xE3, 0xA3 => 0xE4, 0xA1 => 0xE5, 0xA5 => 0xE6, 0x5C => 0xE7,
            0x11 => extended ? (byte)0xE4 : (byte)0xE0,
            0x10 => 0xE1,
            0x12 => extended ? (byte)0xE6 : (byte)0xE2,
            _ => null,
        };
    }

    public static bool IsModifier(byte usage) => usage is >= 0xE0 and <= 0xE7;
}
