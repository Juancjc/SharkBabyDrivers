using SharkBabyDrivers.Core.Devices.X68;

namespace SharkBabyDrivers.Tests;

public class X68ProtocolTests
{
    [Fact]
    public void Bit7_checksum_is_not_of_sum_of_first_seven_bytes()
    {
        var p = X68Protocol.Command([0x8F]);
        Assert.Equal(64, p.Length);
        Assert.Equal(0xFF - 0x8F, p[7]);
        var q = X68Protocol.Command([0x0A, 1, 0xFF, 3, 56, 0, 2]);
        Assert.Equal((byte)(0xFF - ((0x0A + 1 + 0xFF + 3 + 56 + 0 + 2) & 0xFF)), q[7]);
    }

    [Fact]
    public void Bit8_checksum_covers_eight_bytes_for_led()
    {
        var p = X68Protocol.Command([0x07, 4, 1, 4, 0x07, 0xFA, 0xFF, 0xFA], bit8: true);
        Assert.Equal((byte)(0xFF - ((0x07 + 4 + 1 + 4 + 0x07 + 0xFA + 0xFF + 0xFA) & 0xFF)), p[8]);
        Assert.Equal(0xFA, p[7]); // header byte 7 is data, not checksum
    }

    [Fact]
    public void Payload_starts_at_byte_8()
    {
        var p = X68Protocol.Command([0x11], new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, p[8..16]);
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0xAC)]
    [InlineData(0x7F)]
    [InlineData(0xF8)]
    [InlineData(0xBA)]
    [InlineData(0xF0)]
    [InlineData(0xF7)]
    [InlineData(0xFC)]
    [InlineData(0xFF)]
    public void Dangerous_commands_are_refused(byte cmd) =>
        Assert.Throws<InvalidOperationException>(() => X68Protocol.Command([cmd]));

    [Theory]
    [InlineData(8000, 0)]
    [InlineData(1000, 3)]
    [InlineData(125, 6)]
    public void Polling_codes(int hz, byte code)
    {
        Assert.Equal(code, X68Protocol.PollCode(hz));
        Assert.Equal(hz, X68Protocol.PollHz(code));
    }

    [Theory]
    [InlineData(0x0200, 10)]
    [InlineData(0x0300, 100)]
    [InlineData(0x04FF, 100)]
    [InlineData(0x0500, 200)]
    public void Travel_divisor_follows_firmware(int fw, int k) => Assert.Equal(k, X68Protocol.TravelDivisor(fw));

    [Fact]
    public void Macro_round_trips_short_and_long_delays()
    {
        var steps = new List<X68Macro.Step> { new(0x04, true, 30), new(0x04, false, 500), new(X68Macro.MouseLeft, true, 0) };
        var raw = X68Macro.Encode(steps, 3);
        Assert.Equal(new byte[] { 3, 0, 0x04, 0x80 | 30, 0x04, 0x00, 0xF4, 0x01, 0xF0, 0x81 }, raw);
        var (decoded, repeat) = X68Macro.Decode(raw.Concat(new byte[4]).ToArray());
        Assert.Equal(3, repeat);
        Assert.Equal(new X68Macro.Step(0x04, true, 30), decoded[0]);
        Assert.Equal(new X68Macro.Step(0x04, false, 500), decoded[1]);
        Assert.Equal(new X68Macro.Step(X68Macro.MouseLeft, true, 1), decoded[2]); // 0 ms is stored as 1 ms
    }

    [Theory]
    [InlineData(2370u)]
    [InlineData(2901u)]
    public void Layouts_map_every_cap_to_a_unique_slot(uint id)
    {
        var model = X68Model.ForId(id);
        Assert.True(model.Caps.Count >= 66, $"only {model.Caps.Count} caps");
        Assert.Equal(model.Caps.Count, model.Caps.Select(c => c.Slot).Distinct().Count());
        Assert.All(model.Caps, c => Assert.InRange(c.Slot, 0, 127));
        Assert.NotNull(model.FnSlot());
        Assert.Equal(1, model.SlotOf(0x29)); // Esc is slot 1 on both revisions
    }

    [Fact]
    public void Rev2901_has_function_row()
    {
        Assert.Contains(X68Model.Rev2901.Caps, c => c.Label == "F1");
        Assert.DoesNotContain(X68Model.Rev2370.Caps, c => c.Label == "F1");
    }

    [Fact]
    public void Image_snapshot_round_trip_is_lossless()
    {
        var img = new X68Image { DeviceId = 2370, UsbFirmware = 0x0410, ActiveProfile = 2 };
        img.Keys[1][0] = (byte[])X68Model.Rev2370.DefaultKeys.Clone();
        img.SetKeyMode(1, 7, KeyMode.SnapTap, rapidTrigger: true);
        img.SetHeValue(1, X68Protocol.HeOp.Actuation, 7, 123);
        img.Macros[3] = X68Macro.Encode([new(0x05, true, 10)], 1);
        img.LightMode = X68LightMode.Wave;
        img.LightColor = 0xFFFFFF;
        var back = X68Image.FromSnapshot(img.ToSnapshot("k", "m"));
        Assert.Equal(img.Keys[1][0], back.Keys[1][0]);
        Assert.Equal(KeyMode.SnapTap, back.GetKeyMode(1, 7));
        Assert.True(back.GetRapidTrigger(1, 7));
        Assert.Equal(123, back.HeValue(1, X68Protocol.HeOp.Actuation, 7));
        Assert.Equal(img.Macros[3], back.Macros[3]);
        Assert.Equal(2, back.ActiveProfile);
        Assert.Equal(0xFFFFFFu, back.LightColor);   // white travels as FA FF FA
        Assert.Equal(new byte[] { 0xFA, 0xFF, 0xFA }, back.Led[5..8]);
    }

    [Fact]
    public void Referenced_macros_are_found_in_all_layers()
    {
        var img = new X68Image();
        X68Action.Macro(5, X68MacroMode.WhileHeld).Write(img.Keys[3][0], 10);
        X68Action.Macro(2, X68MacroMode.RepeatCount).Write(img.FnMac, 20);
        Assert.Equal(new[] { 2, 5 }, img.ReferencedMacros());
    }
}
