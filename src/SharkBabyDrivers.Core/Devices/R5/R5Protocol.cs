using System.Text.Json;
using SharkBabyDrivers.Core.Storage;

namespace SharkBabyDrivers.Core.Devices.R5;

public sealed class R5Config
{
    public string Model { get; set; } = "Attack Shark R5 Ultra";
    public byte Profile { get; set; } = 1;
    public int[] Dpi { get; set; } = [1200, 2400, 3200, 5600, 8000, 42000];
    public int[] DpiY { get; set; } = [1200, 2400, 3200, 5600, 8000, 42000];
    public byte ActiveDpi { get; set; } = 1;
    public byte Polling { get; set; } = 1;
    public byte Debounce { get; set; }
    public ushort SleepSeconds { get; set; } = 60;
    public byte Lod { get; set; } = 1;
    public bool AngleSnap { get; set; }
    public bool MotionSync { get; set; }
    public bool Ripple { get; set; }
    public bool Tracking { get; set; }
    // Complete raw button payloads preserve assignments this editor does not understand, including macros.
    public byte[][] Buttons { get; set; } = [[1,1,1], [1,1,2], [1,1,3], [1,1,4], [1,1,5]];
    public R5Config Clone() => JsonSerializer.Deserialize<R5Config>(JsonSerializer.Serialize(this))!;
    public void Validate(bool wired)
    {
        if (Model != "Attack Shark R5 Ultra" || Profile is < 1 or > 3 || Dpi is null || DpiY is null
            || Dpi.Length is < 1 or > 6 || DpiY.Length != Dpi.Length
            || Dpi.Concat(DpiY).Any(d => d is < 50 or > 42000 || d % 50 != 0)
            || ActiveDpi < 1 || ActiveDpi > Dpi.Length || !new byte[]{1,2,4,8,32,64,128}.Contains(Polling)
            || (wired && Polling > 8) || Debounce > 30 || Lod is not (1 or 2 or 135)
            || Buttons is null || Buttons.Length != 5 || Buttons.Any(b => b is null || b.Length < 2 || b[1] > 40 || b.Length != b[1]+2)
            || !Buttons.Any(b => b.Length == 3 && b[0] == 1 && b[2] == 1))
            throw new InvalidDataException("Configura��o R5 inv�lida. Confira DPI (50-42000, passos de 50), polling, perfil e pelo menos um clique esquerdo.");
    }
    public static R5Config FromSnapshot(DeviceSnapshot snapshot) =>
        JsonSerializer.Deserialize<R5Config>(snapshot.Notes["r5_config"], JsonStore.Options)
        ?? throw new InvalidDataException("Backup R5 inv�lido.");
}

public static class R5Protocol
{
    public const ushort VendorId = 0x373E, WiredPid = 0x0046, DonglePid = 0x0047;
    // WebHID uses 64 payload bytes and report ID 0. Windows requires the additional report-ID byte.
    public static byte[] Frame(byte length, byte group, byte command, params byte[] payload)
    {
        if (payload.Length > 58) throw new ArgumentOutOfRangeException(nameof(payload));
        var frame = new byte[65];
        frame[3] = 2; frame[4] = length; frame[5] = group; frame[6] = command;
        payload.CopyTo(frame, 7);
        return frame;
    }
    public static byte[] Normalize(byte[] windowsReport)
    {
        if (windowsReport.Length != 65 || windowsReport[0] != 0) throw new IOException("Resposta HID R5 inv�lida.");
        var data = windowsReport[1..];
        if (data[0] is 0xA1 or 2) { var shifted = new byte[64]; data.AsSpan(0,63).CopyTo(shifted.AsSpan(1)); return shifted; }
        return data;
    }
    public static bool Matches(byte[] request, byte[] response) => response.Length == 64
        && response[1] == 0xA1 && response[3] == request[3]
        && response[5] == request[5] && response[6] == request[6];
}
