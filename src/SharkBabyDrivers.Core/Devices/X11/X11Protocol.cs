namespace SharkBabyDrivers.Core.Devices.X11;

/// <summary>
/// Attack Shark X11 (PAW3311) vendor protocol: HID feature reports on interface 2,
/// usage page 0x000B. Layouts and checksums were checked against the official
/// AttackShark.exe (v1.1, class CMS_4_Page and the shared "classic" mouse writer)
/// and cross-checked with the community drivers (mousectl, OpenSharkX11).
/// </summary>
public static class X11Protocol
{
    public const ushort VendorId = 0x1D57;
    public const ushort ProductIdDongle = 0xFA60;
    public const ushort ProductIdWired = 0xFA55;
    /// <summary>Usage page of the config collection (feature reports).</summary>
    public const ushort ConfigUsagePage = 0x000B;
    /// <summary>Usage page of the event collection (battery / DPI button input reports).</summary>
    public const ushort EventUsagePage = 0x000A;

    public const byte RidDpi = 0x04;      // DPI stages, colours, angle snap, ripple, LOD, motion sync
    public const byte RidLight = 0x05;    // lighting mode, colour, speed, brightness, sleep, debounce
    public const byte RidPoll = 0x06;     // polling rate
    public const byte RidButtons = 0x08;  // button assignment (write-only on this firmware)
    public const byte RidMacro = 0x09;    // macro pages (write-only)
    public const byte RidVersion = 0x0B;  // firmware id, READ ONLY (writing it un-pairs the dongle!)
    public const byte RidReset = 0x0C;    // factory reset
    public const byte RidRead = 0xA0;     // read-unlock handshake

    public const int LenDpi = 0x38;       // 56
    public const int LenLight = 0x0F;     // 15
    public const int LenPoll = 0x09;      // 9
    public const int LenButtons = 0x3B;   // 59
    public const int LenVersion = 0x08;   // 8
    public const int LenRead = 0x08;

    public const int Stages = 8;
    public const int MinDpi = 50;
    public const int MaxDpi = 22000;      // limit used by the official UI for the X11
    public const int Buttons = 6;

    /// <summary>Reports that can be read back from the device (in the order the official app reads them).</summary>
    public static readonly (byte Rid, int Length, byte Flag)[] ReadableReports =
    [
        (RidVersion, LenVersion, 0x00),
        (RidPoll, LenPoll, 0x01),
        (RidLight, LenLight, 0x01),
        (RidDpi, LenDpi, 0x01),
    ];

    /// <summary>Read-unlock request: A0 rid len 00 flag 00 00 00.</summary>
    public static byte[] BuildReadRequest(byte rid, int length, byte flag) =>
        [RidRead, rid, (byte)length, 0x00, flag, 0x00, 0x00, 0x00];

    // ---------------------------------------------------------------- checksums

    public static ushort Sum16(ReadOnlySpan<byte> data)
    {
        var sum = 0;
        foreach (var b in data) sum += b;
        return (ushort)sum;
    }

    /// <summary>Report 0x04: 16-bit sum of bytes 3..49, big-endian in 50..51.</summary>
    public static void SealDpi(byte[] r)
    {
        var s = Sum16(r.AsSpan(3, 47));
        r[50] = (byte)(s >> 8);
        r[51] = (byte)s;
    }

    /// <summary>Report 0x05: 16-bit sum of bytes 3..10, big-endian in 11..12.</summary>
    public static void SealLight(byte[] r)
    {
        var s = Sum16(r.AsSpan(3, 8));
        r[11] = (byte)(s >> 8);
        r[12] = (byte)s;
    }

    /// <summary>Report 0x08: 16-bit sum of bytes 3..56, big-endian in 57..58.</summary>
    public static void SealButtons(byte[] r)
    {
        var s = Sum16(r.AsSpan(3, 54));
        r[57] = (byte)(s >> 8);
        r[58] = (byte)s;
    }

    public static bool DpiChecksumOk(byte[] r) => r.Length >= 52 && Sum16(r.AsSpan(3, 47)) == (r[50] << 8 | r[51]);
    public static bool LightChecksumOk(byte[] r) => r.Length >= 13 && Sum16(r.AsSpan(3, 8)) == (r[11] << 8 | r[12]);

    // ---------------------------------------------------------------- polling

    public static readonly int[] PollingRates = [125, 250, 500, 1000];

    public static byte PollCode(int hz) => hz switch
    {
        125 => 0x08,
        250 => 0x04,
        500 => 0x02,
        1000 => 0x01,
        _ => throw new ArgumentOutOfRangeException(nameof(hz), hz, "125/250/500/1000"),
    };

    public static int PollHz(byte code) => code switch
    {
        0x08 => 125,
        0x04 => 250,
        0x02 => 500,
        0x01 => 1000,
        _ => 0,
    };

    public static byte[] BuildPoll(int hz)
    {
        var c = PollCode(hz);
        return [RidPoll, LenPoll, 0x01, c, (byte)(0xFF - c), 0, 0, 0, 0];
    }

    // ---------------------------------------------------------------- DPI codec

    /// <summary>PAW3311 step table, identical to the one embedded in AttackShark.exe @0x4E50A8 (220 entries).</summary>
    public static readonly byte[] DpiTable =
    [
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0e, 0x0f, 0x10, 0x11, 0x12,
        0x13, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x22, 0x23, 0x24, 0x25,
        0x26, 0x27, 0x29, 0x2a, 0x2b, 0x2c, 0x2d, 0x2f, 0x30, 0x31, 0x32, 0x33, 0x34, 0x36, 0x37, 0x38,
        0x39, 0x3a, 0x3b, 0x3d, 0x3e, 0x3f, 0x40, 0x41, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x4a, 0x4b,
        0x4c, 0x4d, 0x4e, 0x4f, 0x51, 0x52, 0x53, 0x54, 0x55, 0x57, 0x58, 0x59, 0x5a, 0x5b, 0x5c, 0x5e,
        0x5f, 0x60, 0x61, 0x62, 0x63, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6b, 0x6c, 0x6d, 0x6e, 0x6f, 0x70,
        0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x79, 0x7a, 0x7b, 0x7c, 0x7d, 0x7f, 0x80, 0x81, 0x82, 0x83,
        0x84, 0x86, 0x87, 0x88, 0x89, 0x8a, 0x8b, 0x8d, 0x8e, 0x8f, 0x90, 0x91, 0x93, 0x94, 0x95, 0x96,
        0x97, 0x98, 0x9a, 0x9b, 0x9c, 0x9d, 0x9e, 0x9f, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5, 0xa7, 0xa8, 0xa9,
        0xaa, 0xab, 0xac, 0xae, 0xaf, 0xb0, 0xb1, 0xb2, 0xb3, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xbb, 0xbc,
        0xbd, 0xbe, 0xbf, 0xc0, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7, 0xc9, 0xca, 0xcb, 0xcc, 0xcd, 0xcf,
        0xd0, 0xd1, 0xd2, 0xd3, 0xd4, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xdb, 0xdd, 0xde, 0xdf, 0xe0, 0xe1,
        0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xea, 0xeb, 0x76, 0x77, 0x79, 0x7a, 0x7b, 0x7c, 0x7d, 0x7f,
        0x80, 0x81, 0x82, 0x83, 0x84, 0x86, 0x87, 0x88, 0x89, 0x8a, 0x8b, 0x8d,
    ];

    /// <summary>Rounds a requested DPI to a value the sensor encodes exactly (50 steps ≤10000, 100 ≤20000, 200 above).</summary>
    public static int NormalizeDpi(int dpi)
    {
        var d = Math.Clamp(dpi, MinDpi, MaxDpi);
        var step = d <= 10000 ? 50 : d <= 20000 ? 100 : 200;
        d = (int)Math.Round(d / (double)step, MidpointRounding.AwayFromZero) * step;
        return Math.Clamp(d, MinDpi, MaxDpi);
    }

    public static (byte X, byte Y, bool Double) EncodeDpi(int dpi)
    {
        var d = NormalizeDpi(dpi);
        if (d == 20100) return (0xEB, 1, true);
        var dbl = false;
        var target = d;
        if (d > 10000)
        {
            target = (int)(d / 2.0 + 0.5);
            dbl = true;
        }
        if (target > 5000 && target % 100 == 0) return (DpiTable[target / 100 - 1], 1, dbl);
        return (DpiTable[(target - 50) / 50], 0, dbl);
    }

    public static int DecodeDpi(byte x, byte y, bool dbl)
    {
        if (x == 0 && y == 0) return 0;
        int value;
        if (y == 0)
        {
            var i = Array.IndexOf(DpiTable, x);
            value = i >= 0 ? i * 50 + 50 : 50;
        }
        else if (y == 1)
        {
            var i = Array.IndexOf(DpiTable, x);
            value = i >= 0 ? i * 100 + 100 : 100;
        }
        else
        {
            value = 10100 + ((y << 8 | x) - 199) * 100;
        }
        if (dbl)
        {
            if (x == 0xEB) return 20100;
            value *= 2;
        }
        return Math.Min(value, 26000);
    }

    // ---------------------------------------------------------------- events

    /// <summary>Parses an input report from the event collection (03 55 …).</summary>
    public static X11Event? ParseEvent(ReadOnlySpan<byte> r)
    {
        if (r.Length < 4 || r[0] != 0x03 || r[1] != 0x55) return null;
        return r[2] switch
        {
            0x40 or 0x41 when r.Length >= 5 => new X11Event.Battery(r[4], r[3] switch { 2 => BatteryState.Full, 3 => BatteryState.Charging, _ => BatteryState.Discharging }),
            0x10 => new X11Event.DpiStage(r[3]),
            _ => null,
        };
    }
}

public enum BatteryState { Discharging, Charging, Full }

public abstract record X11Event
{
    public sealed record Battery(int Percent, BatteryState State) : X11Event;
    public sealed record DpiStage(int Stage) : X11Event;
}
