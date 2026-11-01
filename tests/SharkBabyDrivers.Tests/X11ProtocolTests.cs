using SharkBabyDrivers.Core.Devices.X11;

namespace SharkBabyDrivers.Tests;

public class X11ProtocolTests
{
    // Report from the OpenSharkX11 protocol doc (stage 2 active, default palette). The doc prints byte 49
    // as 00, but its checksum 0x0F68 only adds up with 0x02 there — the value the official app sends.
    private static readonly byte[] SampleDpi = Convert.FromHexString(
        "0438010001" + "3F2020" + "1225384B758100000000000000010000" + "02" +
        "FF000000FF000000FFFFFF0000FFFFFF00FFFF4000FFFFFF" + "02" + "0F68" + "00000000");

    // Captured from a real X11 (firmware 0109) over the 2.4 GHz dongle with SharkBabyDrivers' read handshake.
    private static readonly byte[] RealDpi = Convert.FromHexString(
        "04380100013F20201225384B75810101000000000001000001FF000000FF000000FFFFFF0000FFFFFF00FFFF4000FFFFFF020F6900000000");
    private static readonly byte[] RealLight = Convert.FromHexString("050F010003A800FF00010401AF0000");
    private static readonly byte[] RealPoll = Convert.FromHexString("06090101FE00000000");

    [Fact]
    public void Real_device_reports_have_valid_checksums_and_decode()
    {
        Assert.True(X11Protocol.DpiChecksumOk(RealDpi));
        Assert.True(X11Protocol.LightChecksumOk(RealLight));
        var c = new X11Config(RealDpi, RealLight, RealPoll);
        Assert.Equal([800, 1600, 2400, 3200, 5000, 22000], Enumerable.Range(0, 6).Select(c.GetStageDpi));
        Assert.Equal(6, c.EnabledStageCount);
        Assert.Equal(1, c.ActiveStage);
        Assert.Equal(1000, c.PollingHz);
        Assert.Equal(X11LightMode.Off, c.LightMode);
        Assert.Equal(new Rgb(0, 0xFF, 0), c.LightColor);
        Assert.Equal(6, c.Speed);
        Assert.Equal(8, c.Brightness);
        Assert.Equal(10, c.DeepSleepMinutes);
        Assert.Equal(0.5, c.SleepMinutes);
        Assert.Equal(8, c.DebounceMs);
        Assert.True(c.Ripple);
        Assert.False(c.AngleSnap);
    }

    [Fact]
    public void Unchanged_real_config_rebuilds_identical_frames()
    {
        var c = new X11Config(RealDpi, RealLight, RealPoll);
        Assert.Equal(RealDpi, c.DpiFrame());
        Assert.Equal(RealPoll, c.PollFrame());
        // Off mode: the official writer forces brightness to 8, which is what the device already holds.
        Assert.Equal(RealLight, c.LightFrame());
    }

    [Fact]
    public void Dpi_checksum_matches_captured_report()
    {
        Assert.Equal(56, SampleDpi.Length);
        Assert.True(X11Protocol.DpiChecksumOk(SampleDpi));
    }

    [Fact]
    public void SealDpi_reproduces_captured_checksum()
    {
        var copy = (byte[])SampleDpi.Clone();
        copy[50] = copy[51] = 0;
        X11Protocol.SealDpi(copy);
        Assert.Equal(SampleDpi, copy);
    }

    [Theory]
    [InlineData(125, 0x08, 0xF7)]
    [InlineData(250, 0x04, 0xFB)]
    [InlineData(500, 0x02, 0xFD)]
    [InlineData(1000, 0x01, 0xFE)]
    public void Poll_frames_match_official_bytes(int hz, byte code, byte check)
    {
        Assert.Equal(new byte[] { 0x06, 0x09, 0x01, code, check, 0, 0, 0, 0 }, X11Protocol.BuildPoll(hz));
        Assert.Equal(hz, X11Protocol.PollHz(code));
    }

    [Fact]
    public void Every_normalized_dpi_round_trips()
    {
        for (var dpi = X11Protocol.MinDpi; dpi <= X11Protocol.MaxDpi; dpi += 50)
        {
            var n = X11Protocol.NormalizeDpi(dpi);
            var (x, y, dbl) = X11Protocol.EncodeDpi(n);
            Assert.Equal(n, X11Protocol.DecodeDpi(x, y, dbl));
        }
    }

    [Fact]
    public void Factory_22000_stage_decodes_from_step100_table()
    {
        // mousectl verified this encoding against a real X11's factory 22000 DPI stage.
        Assert.Equal(22000, X11Protocol.DecodeDpi(0x81, 1, true));
        Assert.Equal((0x81, 1, true), ((int)X11Protocol.EncodeDpi(22000).X, (int)X11Protocol.EncodeDpi(22000).Y, X11Protocol.EncodeDpi(22000).Double));
    }

    [Fact]
    public void Sample_report_decodes_to_expected_stages()
    {
        var light = new byte[15];
        light[0] = 0x05; light[1] = 0x0F; light[2] = 1;
        var cfg = new X11Config(SampleDpi, light, X11Protocol.BuildPoll(1000));
        Assert.Equal(2, cfg.ActiveStage);
        Assert.Equal(new Rgb(0, 0xFF, 0), cfg.GetStageColor(1));
        Assert.True(cfg.Ripple);
        Assert.False(cfg.AngleSnap);
        Assert.Equal(6, cfg.EnabledStageCount);
    }

    [Fact]
    public void Light_checksum_is_16bit_big_endian_sum_of_bytes_3_to_10()
    {
        var cfg = X11Config.FactoryDefault();
        cfg.LightMode = X11LightMode.Static;
        cfg.LightColor = new Rgb(0xFF, 0xFF, 0xFF);
        var f = cfg.LightFrame();
        var sum = f.Skip(3).Take(8).Sum(b => b);
        Assert.True(sum > 0xFF); // make sure the high byte is exercised
        Assert.Equal((byte)(sum >> 8), f[11]);
        Assert.Equal((byte)sum, f[12]);
        Assert.True(X11Protocol.LightChecksumOk(f));
    }

    [Fact]
    public void Light_fields_round_trip_without_touching_other_bits()
    {
        var cfg = X11Config.FactoryDefault();
        cfg.Speed = 7;
        cfg.Brightness = 5;
        cfg.DeepSleepMinutes = 37;
        Assert.Equal(7, cfg.Speed);
        Assert.Equal(5, cfg.Brightness);
        Assert.Equal(37, cfg.DeepSleepMinutes);
        cfg.SleepMinutes = 2.5;
        cfg.DebounceMs = 12;
        Assert.Equal(2.5, cfg.SleepMinutes);
        Assert.Equal(12, cfg.DebounceMs);
        Assert.Equal(7, cfg.Speed);
        Assert.Equal(37, cfg.DeepSleepMinutes);
    }

    [Fact]
    public void Brightness_is_forced_to_8_outside_static_modes_like_official_app()
    {
        var cfg = X11Config.FactoryDefault();
        cfg.Brightness = 3;
        cfg.LightMode = X11LightMode.Breathing;
        Assert.Equal(8, cfg.LightFrame()[5] & 0x0F);
        cfg.LightMode = X11LightMode.Static;
        Assert.Equal(3, cfg.LightFrame()[5] & 0x0F);
    }

    [Fact]
    public void Disabling_active_stage_moves_active_to_first_enabled()
    {
        var cfg = X11Config.FactoryDefault();
        cfg.ActiveStage = 3;
        cfg.SetStageEnabled(2, false);
        Assert.Equal(1, cfg.ActiveStage);
    }

    [Fact]
    public void Cannot_disable_last_stage()
    {
        var cfg = X11Config.FactoryDefault();
        for (var i = 1; i < X11Protocol.Stages; i++) cfg.SetStageEnabled(i, false, force: true);
        Assert.Throws<InvalidOperationException>(() => cfg.SetStageEnabled(0, false));
    }

    [Fact]
    public void Button_report_layout_and_checksum()
    {
        var slots = X11Buttons.FactoryDefaults();
        slots[(int)X11Button.Forward] = ButtonAction.Key(KeyModifiers.Ctrl, 0x06);
        var r = X11Buttons.BuildReport(slots);
        Assert.Equal(59, r.Length);
        Assert.Equal(new byte[] { 0x08, 0x3B, 0x01, 0x02, 0, 0, 0x03, 0, 0, 0x04, 0, 0 }, r[..12]);
        Assert.Equal(new byte[] { 0x0D, 0, 0 }, r[18..21]);       // DPI button slot 5
        Assert.Equal(new byte[] { 0x11, 0x01, 0x06 }, r[21..24]); // forward slot 6 → Ctrl+C
        Assert.Equal(new byte[] { 0x09, 0, 0, 0x0A, 0, 0 }, r[51..57]);
        var sum = r.Skip(3).Take(54).Sum(b => b);
        Assert.Equal((byte)(sum >> 8), r[57]);
        Assert.Equal((byte)sum, r[58]);
    }

    [Fact]
    public void Macro_encoding_matches_official_delay_and_release_flags()
    {
        var macro = new MacroDefinition("t", [new MacroStep(0x04, true, 50), new MacroStep(0x04, false, 2000)], MacroPlayMode.RepeatCount, 3);
        var r = X11Macro.BuildLogical(X11Buttons.MacroSlotId(X11Button.Forward), macro);
        Assert.Equal(131, r.Length);
        Assert.Equal(new byte[] { 0x09, 0x83, 7, 0, 0, 0, 0, 3 }, r[..8]);
        Assert.Equal(3, r[28]);                  // 2 steps + 1 extra pair for the long delay
        Assert.Equal(new byte[] { 0x05, 0x04 }, r[29..31]); // 50 ms → 5 | pressed(1)
        // 2000 ms = 10 × 200 ms + 0 → rest clamped to 1 ms → 0 units, released flag 0x81
        Assert.Equal(new byte[] { 0x81, 0x04, 10, 0x03 }, r[31..35]);
        var sum = r.Skip(3).Take(0x7E).Sum(b => b);
        Assert.Equal((byte)(sum >> 8), r[0x81]);
        Assert.Equal((byte)sum, r[0x82]);
    }

    [Fact]
    public void Macro_pages_are_64_bytes_with_page_index()
    {
        var r = X11Macro.BuildLogical(1, new MacroDefinition("t", [new MacroStep(0x04, true, 10)]));
        var pages = X11Macro.Paginate(r);
        Assert.Equal(3, pages.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(64, pages[i].Length);
            Assert.Equal(new byte[] { 0x09, 0x40, 1, (byte)i }, pages[i][..4]);
        }
        Assert.Equal(r[3..63], pages[0][4..64]);
        Assert.Equal(r[123..131], pages[2][4..12]);
    }

    [Theory]
    [InlineData("0355400164", 100)]
    [InlineData("035541033C", 60)]
    public void Battery_events_parse(string hex, int percent)
    {
        var e = Assert.IsType<X11Event.Battery>(X11Protocol.ParseEvent(Convert.FromHexString(hex)));
        Assert.Equal(percent, e.Percent);
    }

    [Fact]
    public void Dpi_button_event_parses()
    {
        var e = Assert.IsType<X11Event.DpiStage>(X11Protocol.ParseEvent(Convert.FromHexString("0355100300")));
        Assert.Equal(3, e.Stage);
    }
}
