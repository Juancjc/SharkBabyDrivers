namespace SharkBabyDrivers.Core.Devices.X68;

/// <summary>
/// Attack Shark X68 PRO HE (RY5088 platform) vendor protocol, reverse-engineered from the official
/// "Attack Shark Driver v4" 3.1.12 (Electron renderer + iot_driver.exe). 64-byte feature reports with
/// report id 0 on interface 2 (usage page 0xFFFF, usage 0x02).
/// </summary>
public static class X68Protocol
{
    public const ushort VendorId = 0x3151;
    public const ushort PidX68ProHeWired = 0x502F;
    public const ushort PidX68HeWired = 0x502D;
    public const ushort PidDongle = 0x5026;
    public const ushort ConfigUsagePage = 0xFFFF;
    public const ushort ConfigUsage = 0x0002;
    public const ushort EventUsage = 0x0001;

    public const int PacketLength = 64;
    public const int Slots = 128;
    public const int MatrixBytes = Slots * 4;      // 512
    public const int Profiles = 4;
    public const int SonProfiles = 4;
    public const int PageData = 56;

    // ---- command bytes (GET = SET | 0x80) ----
    public const byte SetReset = 0x01;            // documented only; blocked by the app
    public const byte SetReport = 0x03, GetReport = 0x83;
    public const byte SetProfile = 0x04, GetProfile = 0x84;
    public const byte GetLedOnOff = 0x85;
    public const byte SetDebounce = 0x06, GetDebounce = 0x86;
    public const byte SetLedParam = 0x07, GetLedParam = 0x87;
    public const byte SetKbOption = 0x09, GetKbOption = 0x89;
    public const byte SetKeyMatrix = 0x0A, GetKeyMatrix = 0x8A;
    public const byte SetMacro = 0x0B, GetMacro = 0x8B;
    public const byte SetUserPic = 0x0C, GetUserPic = 0x8C;
    public const byte SetScreenColor = 0x0E;      // R,G,B for mode 21 (ScreenColor); streamed, not stored in flash
    public const byte SetFn = 0x10, GetFn = 0x90;
    public const byte SetSleepTime = 0x11, GetSleepTime = 0x91;
    public const byte SetAutoOs = 0x17, GetAutoOs = 0x97;
    public const byte SetMagnetismReport = 0x1B;
    public const byte SetMagnetismCal = 0x1C;
    public const byte SetMagnetismMaxCal = 0x1E;
    public const byte SetMultiMagnetism = 0x65, GetMultiMagnetism = 0xE5;
    public const byte GetRfVersion = 0x80;
    public const byte GetUsbVersion = 0x8F;

    /// <summary>Commands this app must never emit (reset, flash erase, pairing, boot loaders, firmware upgrade).</summary>
    public static readonly byte[] Forbidden = [SetReset, 0xAC, 0x7F, 0xF8, 0xBA];

    /// <summary>"Bit7" checksum: byte 7 = 0xFF − (sum(bytes 0..6) &amp; 0xFF).</summary>
    public static void SealBit7(byte[] p) => p[7] = (byte)(0xFF - (Sum(p, 0, 7) & 0xFF));

    /// <summary>"Bit8" checksum used by the LED command: byte 8 = 0xFF − (sum(bytes 0..7) &amp; 0xFF).</summary>
    public static void SealBit8(byte[] p) => p[8] = (byte)(0xFF - (Sum(p, 0, 8) & 0xFF));

    private static int Sum(byte[] p, int from, int count)
    {
        var s = 0;
        for (var i = from; i < from + count; i++) s += p[i];
        return s;
    }

    /// <summary>Builds a 64-byte command with Bit7 checksum (header = first ≤7 bytes, payload from byte 8).</summary>
    public static byte[] Command(ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload = default, bool bit8 = false)
    {
        if (header.Length == 0) throw new ArgumentException("comando vazio");
        if (header[0] >= 0xF0 || Array.IndexOf(Forbidden, header[0]) >= 0) throw new InvalidOperationException($"Comando 0x{header[0]:X2} bloqueado por segurança.");
        var p = new byte[PacketLength];
        header[..Math.Min(header.Length, bit8 ? 8 : 7)].CopyTo(p);
        payload.CopyTo(p.AsSpan(bit8 ? 9 : 8));
        if (bit8) SealBit8(p);
        else SealBit7(p);
        return p;
    }

    // ---- polling rate codes ----
    public static readonly int[] PollingRates = [8000, 4000, 2000, 1000, 500, 250, 125];
    public static int PollHz(byte code) => code < PollingRates.Length ? PollingRates[code] : 0;
    public static byte PollCode(int hz) => (byte)Math.Max(0, Array.IndexOf(PollingRates, hz));

    // ---- Hall-effect tables (command 0x65 / 0xE5 "op") ----
    public enum HeOp : byte
    {
        Actuation = 0,         // u16, travel × k
        Release = 1,           // u16
        RtPress = 2,           // u16, Rapid Trigger press sensitivity
        RtRelease = 3,         // u16
        DksTravel = 4,         // u16
        MtTime = 5,            // u8, × 10 ms
        BottomDeadZone = 6,    // u16
        KeyMode = 7,           // u8: bit7 RT, low bits mode
        DksStageKey = 8,       // per-key only, 4 bytes
        SnapPartner = 9,       // u8 slot index
        DksStages = 10,        // GET only: 4 × 128 bytes
        TopDeadZone = 251,     // u8 (fw ≥ 0x400)
        SwitchType = 252,      // u8
        LiveTravel = 254,      // GET only (calibration readout)
    }

    public static bool IsU16(HeOp op) => op is HeOp.Actuation or HeOp.Release or HeOp.RtPress or HeOp.RtRelease
        or HeOp.DksTravel or HeOp.BottomDeadZone or HeOp.LiveTravel;

    /// <summary>Pages returned by a bulk GET of an op (64 raw bytes each).</summary>
    public static int ReadPages(HeOp op) => op == HeOp.DksStages ? 8 : IsU16(op) ? 4 : 2;

    /// <summary>Default fill used by the official writer for slots that do not exist.</summary>
    public static int DefaultFill(HeOp op) => op switch
    {
        HeOp.Actuation => 200,
        HeOp.Release => 280,
        HeOp.BottomDeadZone => 30,
        HeOp.TopDeadZone => 30,
        _ => 0,
    };

    /// <summary>Ops that make up a profile's Hall-effect configuration, in the order they are read.</summary>
    public static readonly HeOp[] ProfileOps =
    [
        HeOp.KeyMode, HeOp.Actuation, HeOp.Release, HeOp.RtPress, HeOp.RtRelease, HeOp.BottomDeadZone,
        HeOp.DksTravel, HeOp.MtTime, HeOp.SnapPartner, HeOp.TopDeadZone, HeOp.SwitchType, HeOp.DksStages,
    ];

    /// <summary>Travel unit divisor from firmware version: values are stored in 1/k mm.</summary>
    public static int TravelDivisor(int fw) => fw < 0x300 ? 10 : fw < 0x500 ? 100 : 200;
    public static bool SupportsTopDeadZone(int fw) => fw >= 0x400;
}

public enum KeyMode : byte
{
    Normal = 0,
    Dks = 2,
    ModTap = 3,
    ToggleHold = 4,
    ToggleRepeat = 5,
    SnapTap = 7,
}

/// <summary>Lighting effects (ids of the RY5088 firmware) offered by the official app for the X68.</summary>
public enum X68LightMode : byte
{
    Off = 0, AlwaysOn = 1, Breath = 2, Neon = 3, Wave = 4, Ripple = 5, Raindrop = 6, Snake = 7, PressAction = 8,
    Converge = 9, SineWave = 10, Kaleidoscope = 11, LineWave = 12, UserPicture = 13, Laser = 14, CircleWave = 15,
    Dazzling = 16, RainDown = 17, Meteor = 18, PressActionOff = 19, MusicFollow3 = 20, ScreenColor = 21,
    MusicFollow2 = 22, Train = 23, FireWorks = 24, UserColor = 25,
}

public sealed record LightModeInfo(X68LightMode Mode, int Options, bool HasSpeed, bool HasColor);

public static class X68Lighting
{
    public const int MaxBrightness = 4;
    public const int MaxSpeed = 4;
    public const byte Rainbow = 8, CustomColor = 7;

    /// <summary>The effects the official app lists for the X68 (layout "I"), host-driven ones excluded.</summary>
    public static readonly IReadOnlyList<LightModeInfo> Modes =
    [
        new(X68LightMode.Off, 0, false, false),
        new(X68LightMode.AlwaysOn, 0, false, true),
        new(X68LightMode.Breath, 0, true, true),
        new(X68LightMode.Neon, 0, true, false),
        new(X68LightMode.Wave, 4, true, true),
        new(X68LightMode.Ripple, 0, true, true),
        new(X68LightMode.Raindrop, 0, true, true),
        new(X68LightMode.Snake, 2, true, true),
        new(X68LightMode.PressAction, 0, true, true),
        new(X68LightMode.Converge, 0, true, true),
        new(X68LightMode.SineWave, 0, true, true),
        new(X68LightMode.Kaleidoscope, 2, true, true),
        new(X68LightMode.PressActionOff, 0, true, true),
        new(X68LightMode.LineWave, 2, true, true),
        new(X68LightMode.Laser, 0, true, true),
        new(X68LightMode.CircleWave, 2, true, true),
        new(X68LightMode.Dazzling, 0, true, true),
        new(X68LightMode.RainDown, 0, true, true),
        new(X68LightMode.Meteor, 0, true, true),
        new(X68LightMode.UserPicture, 5, false, false),
    ];

    public static readonly uint[] PresetColors = [0xFF0000, 0xFF8000, 0xFFFF00, 0x00FF00, 0x00FFFF, 0x0000FF, 0xFF00FF];
}
