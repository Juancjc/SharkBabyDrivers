using static SharkBabyDrivers.Core.Devices.X68.X68Protocol;

namespace SharkBabyDrivers.Core.Devices.X68;

/// <summary>
/// Everything that lives in the keyboard's flash, as raw bytes exactly as the firmware returns them.
/// A backup is this image; editing changes these bytes; writing replays them with the SET commands.
/// </summary>
public sealed class X68Image
{
    public uint DeviceId { get; set; }
    public int UsbFirmware { get; set; }
    public int RfFirmware { get; set; }
    public int ActiveProfile { get; set; }

    /// <summary>Raw GET responses (64 bytes) of the single-value settings.</summary>
    public byte[] Led { get; set; } = new byte[PacketLength];
    public byte[] KbOption { get; set; } = new byte[PacketLength];
    public byte[] Sleep { get; set; } = new byte[PacketLength];
    public byte[] Report { get; set; } = new byte[PacketLength];
    public byte[] AutoOs { get; set; } = new byte[PacketLength];
    public byte[] Debounce { get; set; } = new byte[PacketLength];

    public byte[] FnWin { get; set; } = new byte[MatrixBytes];
    public byte[] FnMac { get; set; } = new byte[MatrixBytes];
    public byte[][] UserPics { get; set; } = Enumerable.Range(0, 5).Select(_ => new byte[384]).ToArray();

    /// <summary>[profile][sonProfile]  512-byte key matrix.</summary>
    public byte[][][] Keys { get; set; } = NewMatrices();

    /// <summary>[profile]  op  raw table.</summary>
    public Dictionary<HeOp, byte[]>[] HallEffect { get; set; } =
        Enumerable.Range(0, Profiles).Select(_ => new Dictionary<HeOp, byte[]>()).ToArray();

    /// <summary>macro index  raw macro buffer (up to 256 bytes).</summary>
    public SortedDictionary<int, byte[]> Macros { get; set; } = new();

    private static byte[][][] NewMatrices() =>
        Enumerable.Range(0, Profiles).Select(_ => Enumerable.Range(0, SonProfiles).Select(_ => new byte[MatrixBytes]).ToArray()).ToArray();

    public int Firmware => RfFirmware != 0 ? RfFirmware : UsbFirmware;
    public int Divisor => TravelDivisor(Firmware);
    public bool HasTopDeadZone => SupportsTopDeadZone(Firmware);
    public X68Model Model => X68Model.ForId(DeviceId);

    public X68Image Clone()
    {
        var c = (X68Image)MemberwiseClone();
        c.Led = (byte[])Led.Clone();
        c.KbOption = (byte[])KbOption.Clone();
        c.Sleep = (byte[])Sleep.Clone();
        c.Report = (byte[])Report.Clone();
        c.AutoOs = (byte[])AutoOs.Clone();
        c.Debounce = (byte[])Debounce.Clone();
        c.FnWin = (byte[])FnWin.Clone();
        c.FnMac = (byte[])FnMac.Clone();
        c.UserPics = UserPics.Select(p => (byte[])p.Clone()).ToArray();
        c.Keys = Keys.Select(p => p.Select(s => (byte[])s.Clone()).ToArray()).ToArray();
        c.HallEffect = HallEffect.Select(d => d.ToDictionary(kv => kv.Key, kv => (byte[])kv.Value.Clone())).ToArray();
        c.Macros = new SortedDictionary<int, byte[]>(Macros.ToDictionary(kv => kv.Key, kv => (byte[])kv.Value.Clone()));
        return c;
    }

    /// <summary>Restores only the physical active profile, retaining global settings and other profiles.</summary>
    public X68Image WithActiveProfileRestored(X68Image original)
    {
        if (DeviceId != original.DeviceId) throw new InvalidDataException("Backup de outro teclado.");
        var restored = Clone();
        restored.Keys[ActiveProfile] = original.Keys[ActiveProfile].Select(k => k.ToArray()).ToArray();
        if (X68Device.HeWasRead(original, ActiveProfile))
            restored.HallEffect[ActiveProfile] = original.HallEffect[ActiveProfile]
                .ToDictionary(k => k.Key, k => k.Value.ToArray());
        return restored;
    }

    // ------------------------------------------------------------------ snapshot (backup) mapping

    public DeviceSnapshot ToSnapshot(string deviceKey, string model, ushort? productId = null)
    {
        var s = new DeviceSnapshot
        {
            Model = model, DeviceKey = deviceKey, VendorId = VendorId, ProductId = productId ?? Model.WiredProductId,
            Firmware = $"usb {UsbFirmware:X4} rf {RfFirmware:X4} id {DeviceId}",
        };
        s.Notes["device_id"] = DeviceId.ToString();
        s.Notes["usb_fw"] = UsbFirmware.ToString();
        s.Notes["rf_fw"] = RfFirmware.ToString();
        s.Notes["active_profile"] = ActiveProfile.ToString();
        s.Put("led", Led); s.Put("kb_option", KbOption); s.Put("sleep", Sleep); s.Put("report", Report);
        s.Put("auto_os", AutoOs); s.Put("debounce", Debounce);
        s.Put("fn_win", FnWin); s.Put("fn_mac", FnMac);
        for (var i = 0; i < UserPics.Length; i++) s.Put($"user_pic_{i}", UserPics[i]);
        for (var p = 0; p < Profiles; p++)
        {
            for (var son = 0; son < SonProfiles; son++) s.Put($"keys_p{p}_s{son}", Keys[p][son]);
            foreach (var (op, data) in HallEffect[p]) s.Put($"he_p{p}_op{(int)op}", data);
        }
        foreach (var (idx, data) in Macros) s.Put($"macro_{idx}", data);
        return s;
    }

    public static X68Image FromSnapshot(DeviceSnapshot s)
    {
        byte[] Need(string name) => s.Get(name) ?? throw new InvalidDataException($"backup sem '{name}'");
        var img = new X68Image
        {
            DeviceId = uint.Parse(s.Notes.GetValueOrDefault("device_id", "0")),
            UsbFirmware = int.Parse(s.Notes.GetValueOrDefault("usb_fw", "0")),
            RfFirmware = int.Parse(s.Notes.GetValueOrDefault("rf_fw", "0")),
            ActiveProfile = int.Parse(s.Notes.GetValueOrDefault("active_profile", "0")),
            Led = Need("led"), KbOption = Need("kb_option"), Sleep = Need("sleep"), Report = Need("report"),
            AutoOs = Need("auto_os"), Debounce = Need("debounce"), FnWin = Need("fn_win"), FnMac = Need("fn_mac"),
        };
        for (var i = 0; i < img.UserPics.Length; i++) img.UserPics[i] = s.Get($"user_pic_{i}") ?? img.UserPics[i];
        for (var p = 0; p < Profiles; p++)
        {
            for (var son = 0; son < SonProfiles; son++) img.Keys[p][son] = Need($"keys_p{p}_s{son}");
            foreach (var op in ProfileOps)
                if (s.Get($"he_p{p}_op{(int)op}") is { } data) img.HallEffect[p][op] = data;
        }
        foreach (var (name, _) in s.Reports.Where(kv => kv.Key.StartsWith("macro_", StringComparison.Ordinal)))
            img.Macros[int.Parse(name["macro_".Length..])] = s.Get(name)!;
        return img;
    }

    // ------------------------------------------------------------------ typed accessors used by the UI

    public int PollingHz { get => PollHz(Report[2]); set => Report[2] = PollCode(value); }
    public int DebounceValue { get => Debounce[1]; set => Debounce[1] = (byte)Math.Clamp(value, 0, 255); }
    public bool AutoOsDetect { get => AutoOs[1] == 1; set => AutoOs[1] = (byte)(value ? 1 : 0); }

    public bool AntiMistouch { get => KbOption[3] != 0; set => KbOption[3] = (byte)(value ? 1 : 0); }
    public int RtStability { get => KbOption[4] > 5 ? 0 : KbOption[4] * 25; set => KbOption[4] = (byte)Math.Clamp(value / 25, 0, 5); }
    public bool WasdSwap { get => KbOption[5] == 1; set => KbOption[5] = (byte)(value ? 1 : 0); }
    public int FnLayerIndex { get => KbOption[2]; set => KbOption[2] = (byte)value; }
    /// <summary>0 Windows, 1 Mac, 2 iOS, 3 Android (read-only; follows the keyboard's OS switch).</summary>
    public int OsMode => KbOption[1];

    public int SleepSeconds24 { get => U16(Sleep, 10); set => PutU16(Sleep, 10, value); }
    public int DeepSleepSeconds24 { get => U16(Sleep, 14); set => PutU16(Sleep, 14, value); }
    public int SleepSecondsBt { get => U16(Sleep, 8); set => PutU16(Sleep, 8, value); }
    public int DeepSleepSecondsBt { get => U16(Sleep, 12); set => PutU16(Sleep, 12, value); }

    public X68LightMode LightMode { get => (X68LightMode)Led[1]; set => Led[1] = (byte)value; }
    public int LightSpeed { get => Math.Clamp(X68Lighting.MaxSpeed - Led[2], 0, X68Lighting.MaxSpeed); set => Led[2] = (byte)(X68Lighting.MaxSpeed - Math.Clamp(value, 0, X68Lighting.MaxSpeed)); }
    public int LightBrightness { get => Math.Clamp((int)Led[3], 0, X68Lighting.MaxBrightness); set => Led[3] = (byte)Math.Clamp(value, 0, X68Lighting.MaxBrightness); }
    public int LightOption { get => Led[4] >> 4; set => Led[4] = (byte)((value << 4) | (Led[4] & 0x0F)); }
    public bool LightRainbow { get => (Led[4] & 0x0F) == X68Lighting.Rainbow; set => Led[4] = (byte)((Led[4] & 0xF0) | (value ? X68Lighting.Rainbow : X68Lighting.CustomColor)); }

    public uint LightColor
    {
        get
        {
            var low = Led[4] & 0x0F;
            if (low < X68Lighting.PresetColors.Length && LightMode != X68LightMode.UserPicture) return X68Lighting.PresetColors[low];
            var c = (uint)(Led[5] << 16 | Led[6] << 8 | Led[7]);
            return c == 0xFAFFFA ? 0xFFFFFF : c;
        }
        set
        {
            var c = value == 0xFFFFFF ? 0xFAFFFAu : value;
            Led[5] = (byte)(c >> 16); Led[6] = (byte)(c >> 8); Led[7] = (byte)c;
            if ((Led[4] & 0x0F) != X68Lighting.Rainbow) Led[4] = (byte)((Led[4] & 0xF0) | X68Lighting.CustomColor);
        }
    }

    // Hall effect per key ---------------------------------------------------------

    public byte[] He(int profile, HeOp op)
    {
        if (!HallEffect[profile].TryGetValue(op, out var t))
        {
            t = new byte[op == HeOp.DksStages ? 512 : IsU16(op) ? 256 : 128];
            var fill = DefaultFill(op);
            if (fill != 0)
                for (var i = 0; i < Slots; i++)
                    if (IsU16(op)) PutU16(t, i * 2, fill); else t[i] = (byte)fill;
            HallEffect[profile][op] = t;
        }
        return t;
    }

    public int HeValue(int profile, HeOp op, int slot) => IsU16(op) ? U16(He(profile, op), slot * 2) : He(profile, op)[slot];

    public void SetHeValue(int profile, HeOp op, int slot, int value)
    {
        if (IsU16(op)) PutU16(He(profile, op), slot * 2, value);
        else He(profile, op)[slot] = (byte)Math.Clamp(value, 0, 255);
    }

    public KeyMode GetKeyMode(int profile, int slot) => (KeyMode)(He(profile, HeOp.KeyMode)[slot] & 0x7F);
    public bool GetRapidTrigger(int profile, int slot) => (He(profile, HeOp.KeyMode)[slot] & 0x80) != 0;

    public void SetKeyMode(int profile, int slot, KeyMode mode, bool rapidTrigger) =>
        He(profile, HeOp.KeyMode)[slot] = (byte)((byte)mode | (rapidTrigger ? 0x80 : 0));

    public double Mm(int raw) => raw / (double)Divisor;
    public int Raw(double mm) => (int)Math.Round(mm * Divisor);

    /// <summary>DKS stage byte for stage-key t (0..3) of a slot: four 2-bit phase fields.</summary>
    public byte DksStage(int profile, int slot, int t) => He(profile, HeOp.DksStages)[t * Slots + slot];
    public void SetDksStage(int profile, int slot, int t, byte value) => He(profile, HeOp.DksStages)[t * Slots + slot] = value;

    public static int U16(byte[] b, int at) => b[at] | b[at + 1] << 8;
    public static void PutU16(byte[] b, int at, int v)
    {
        b[at] = (byte)v;
        b[at + 1] = (byte)(v >> 8);
    }

    /// <summary>Macro indexes referenced by any key / Fn matrix (these are the macros worth backing up).</summary>
    public IEnumerable<int> ReferencedMacros()
    {
        var all = Keys.SelectMany(p => p).Append(FnWin).Append(FnMac);
        return all.SelectMany(m => Enumerable.Range(0, Slots).Select(i => X68Action.Read(m, i)))
            .Where(a => a.Kind == X68ActionKind.Macro)
            .Select(a => (int)a.B2)
            .Distinct()
            .OrderBy(i => i);
    }
}
