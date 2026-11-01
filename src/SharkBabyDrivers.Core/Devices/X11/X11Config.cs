namespace SharkBabyDrivers.Core.Devices.X11;

public enum X11LightMode : byte
{
    Off = 0,
    Static = 1,
    Breathing = 2,
    Neon = 3,
    ColorBreathing = 4,
    StaticDpi = 5,
    BreathingDpi = 6,
}

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    public static Rgb Parse(string hex)
    {
        var h = hex.TrimStart('#');
        return new Rgb(Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16));
    }
}

/// <summary>
/// Editable view over the three readable configuration reports of the X11.
/// Every setter changes only its own bits, so bytes whose meaning is unknown
/// survive a read-modify-write untouched.
/// </summary>
public sealed class X11Config
{
    public byte[] Dpi { get; }
    public byte[] Light { get; }
    public byte[] Poll { get; }

    public X11Config(byte[] dpi, byte[] light, byte[] poll)
    {
        if (dpi.Length < X11Protocol.LenDpi || dpi[0] != X11Protocol.RidDpi) throw new ArgumentException("report 0x04 inválido", nameof(dpi));
        if (light.Length < 13 || light[0] != X11Protocol.RidLight) throw new ArgumentException("report 0x05 inválido", nameof(light));
        if (poll.Length < 5 || poll[0] != X11Protocol.RidPoll) throw new ArgumentException("report 0x06 inválido", nameof(poll));
        Dpi = dpi[..X11Protocol.LenDpi];
        Light = light.Length >= X11Protocol.LenLight ? light[..X11Protocol.LenLight] : Pad(light, X11Protocol.LenLight);
        Poll = poll.Length >= X11Protocol.LenPoll ? poll[..X11Protocol.LenPoll] : Pad(poll, X11Protocol.LenPoll);
    }

    private static byte[] Pad(byte[] src, int len)
    {
        var r = new byte[len];
        src.CopyTo(r, 0);
        return r;
    }

    public X11Config Clone() => new((byte[])Dpi.Clone(), (byte[])Light.Clone(), (byte[])Poll.Clone());

    /// <summary>Factory configuration of the X11 (used only when the device cannot be read).</summary>
    public static X11Config FactoryDefault()
    {
        var dpi = new byte[X11Protocol.LenDpi];
        dpi[0] = X11Protocol.RidDpi; dpi[1] = X11Protocol.LenDpi; dpi[2] = 0x01;
        var light = new byte[X11Protocol.LenLight];
        light[0] = X11Protocol.RidLight; light[1] = X11Protocol.LenLight; light[2] = 0x01;
        var cfg = new X11Config(dpi, light, X11Protocol.BuildPoll(1000));
        int[] stages = [800, 1600, 2400, 3200, 5000, 22000];
        Rgb[] colors = [new(255, 0, 0), new(0, 0, 255), new(0, 255, 0), new(255, 0, 255), new(0, 255, 255), new(255, 255, 0), new(255, 255, 255), new(255, 128, 0)];
        for (var i = 0; i < X11Protocol.Stages; i++)
        {
            cfg.SetStageEnabled(i, i < stages.Length, force: true);
            cfg.SetStageDpi(i, i < stages.Length ? stages[i] : 800);
            cfg.SetStageColor(i, colors[i]);
        }
        cfg.ActiveStage = 1;
        cfg.Ripple = true;
        cfg.LightMode = X11LightMode.BreathingDpi;
        cfg.LightColor = new Rgb(0, 128, 255);
        cfg.Brightness = 8;
        cfg.Speed = 3;
        cfg.SleepMinutes = 1;
        cfg.DeepSleepMinutes = 10;
        cfg.DebounceMs = 8;
        return cfg;
    }

    // ------------------------------------------------------------------ DPI (report 0x04)

    public bool AngleSnap { get => Dpi[3] != 0; set => Dpi[3] = (byte)(value ? 1 : 0); }
    public bool Ripple { get => Dpi[4] != 0; set => Dpi[4] = (byte)(value ? 1 : 0); }

    public bool IsStageEnabled(int i) => (Dpi[5] & (1 << i)) != 0;

    public void SetStageEnabled(int i, bool on, bool force = false)
    {
        if (on)
        {
            Dpi[5] |= (byte)(1 << i);
            if (Dpi[8 + i] == 0 && Dpi[16 + i] == 0) SetStageDpi(i, 800);
            return;
        }
        if (!force && EnabledStageCount <= 1) throw new InvalidOperationException("Pelo menos um estágio de DPI precisa ficar ativo.");
        Dpi[5] &= (byte)~(1 << i);
        if (ActiveStage == i + 1)
        {
            var first = Enumerable.Range(0, X11Protocol.Stages).FirstOrDefault(IsStageEnabled);
            Dpi[24] = (byte)(first + 1);
        }
    }

    public int EnabledStageCount => Enumerable.Range(0, X11Protocol.Stages).Count(IsStageEnabled);

    public int GetStageDpi(int i) => X11Protocol.DecodeDpi(Dpi[8 + i], Dpi[16 + i], (Dpi[6] & (1 << i)) != 0);

    public void SetStageDpi(int i, int dpi)
    {
        var (x, y, dbl) = X11Protocol.EncodeDpi(dpi);
        Dpi[8 + i] = x;
        Dpi[16 + i] = y;
        Dpi[6] = dbl ? (byte)(Dpi[6] | (1 << i)) : (byte)(Dpi[6] & ~(1 << i));
        Dpi[7] = Dpi[6];
    }

    public Rgb GetStageColor(int i) => new(Dpi[25 + i * 3], Dpi[26 + i * 3], Dpi[27 + i * 3]);

    public void SetStageColor(int i, Rgb c)
    {
        Dpi[25 + i * 3] = c.R;
        Dpi[26 + i * 3] = c.G;
        Dpi[27 + i * 3] = c.B;
    }

    /// <summary>Active DPI stage, 1-based.</summary>
    public int ActiveStage
    {
        get => Dpi[24];
        set
        {
            if (value < 1 || value > X11Protocol.Stages) throw new ArgumentOutOfRangeException(nameof(value));
            if (!IsStageEnabled(value - 1)) throw new InvalidOperationException($"O estágio {value} está desativado.");
            Dpi[24] = (byte)value;
        }
    }

    // ------------------------------------------------------------------ lighting / power (report 0x05)

    public X11LightMode LightMode { get => (X11LightMode)(Light[3] >> 4); set => Light[3] = (byte)((byte)value << 4 | (Light[3] & 0x0F)); }

    /// <summary>Animation speed 1 (slow) … 8 (fast); the firmware stores 9 − speed.</summary>
    public int Speed
    {
        get => Math.Clamp(9 - (Light[4] & 0x0F), 1, 8);
        set => Light[4] = (byte)((Light[4] & 0xF0) | (9 - Math.Clamp(value, 1, 8)));
    }

    /// <summary>Brightness 1…8 (the official app only applies it to the static modes).</summary>
    public int Brightness
    {
        get => Math.Clamp(Light[5] & 0x0F, 1, 8);
        set => Light[5] = (byte)((Light[5] & 0xF0) | Math.Clamp(value, 1, 8));
    }

    /// <summary>Deep-sleep timer in minutes (1…60), split over the high nibbles of bytes 4 and 5.</summary>
    public int DeepSleepMinutes
    {
        get => (Light[4] & 0xF0) | (Light[5] >> 4);
        set
        {
            var v = Math.Clamp(value, 1, 60);
            Light[4] = (byte)((v & 0xF0) | (Light[4] & 0x0F));
            Light[5] = (byte)(((v & 0x0F) << 4) | (Light[5] & 0x0F));
        }
    }

    public Rgb LightColor
    {
        get => new(Light[6], Light[7], Light[8]);
        set { Light[6] = value.R; Light[7] = value.G; Light[8] = value.B; }
    }

    /// <summary>Light-off / sleep timer in minutes, 0.5 … 30 in 0.5 steps.</summary>
    public double SleepMinutes
    {
        get => Light[9] / 2.0;
        set => Light[9] = (byte)Math.Clamp((int)Math.Round(value * 2), 1, 60);
    }

    /// <summary>Key response (debounce) in ms, 4 … 50 in steps of 2.</summary>
    public int DebounceMs
    {
        get => Light[10] * 2;
        set => Light[10] = (byte)Math.Clamp(value / 2, 2, 25);
    }

    // ------------------------------------------------------------------ polling (report 0x06)

    public int PollingHz
    {
        get => X11Protocol.PollHz(Poll[3]);
        set
        {
            var c = X11Protocol.PollCode(value);
            Poll[3] = c;
            Poll[4] = (byte)(0xFF - c);
        }
    }

    // ------------------------------------------------------------------ wire frames

    /// <summary>Report 0x04 ready to send (byte 49 = 0x02 like the official app, checksum sealed).</summary>
    public byte[] DpiFrame()
    {
        var f = (byte[])Dpi.Clone();
        f[0] = X11Protocol.RidDpi; f[1] = X11Protocol.LenDpi; f[2] = 0x01;
        f[49] = 0x02;
        X11Protocol.SealDpi(f);
        return f;
    }

    public byte[] LightFrame()
    {
        var f = (byte[])Light.Clone();
        f[0] = X11Protocol.RidLight; f[1] = X11Protocol.LenLight; f[2] = 0x01;
        // The official writer forces the brightness nibble to 8 outside the static modes.
        if (LightMode is not (X11LightMode.Static or X11LightMode.StaticDpi))
            f[5] = (byte)((f[5] & 0xF0) | 0x08);
        X11Protocol.SealLight(f);
        return f;
    }

    public byte[] PollFrame() => X11Protocol.BuildPoll(PollingHz == 0 ? 1000 : PollingHz);

    public bool SameAs(X11Config other) =>
        DpiFrame().AsSpan(3, 47).SequenceEqual(other.DpiFrame().AsSpan(3, 47))
        && LightFrame().AsSpan(3, 8).SequenceEqual(other.LightFrame().AsSpan(3, 8))
        && Poll[3] == other.Poll[3];
}
