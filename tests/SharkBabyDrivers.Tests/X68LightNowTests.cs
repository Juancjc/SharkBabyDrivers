using SharkBabyDrivers.Core.Devices.X68;
using SharkBabyDrivers.Localization;
using SharkBabyDrivers.ViewModels;

namespace SharkBabyDrivers.Tests;

/// <summary>Shares the collection of the WPF tests because both depend on the global UI language.</summary>
[Collection("Mouse WPF")]
public sealed class X68LightNowTests
{
    private static T InPortuguese<T>(Func<T> body)
    {
        var previous = Loc.I.Language;
        Loc.I.Language = "pt-BR";
        try { return body(); }
        finally { Loc.I.Language = previous; }
    }

    private static IReadOnlyList<KeyCap> Caps => X68Model.Rev2901.Caps;

    [Fact]
    public void Static_colour_is_described_and_lights_every_key_with_it()
    {
        var img = new X68Image { LightMode = X68LightMode.AlwaysOn, LightBrightness = 4 };
        img.LightColor = 0x12ABEF;
        var now = InPortuguese(() => X68LightNow.From(img, signalRgbOwns: false, signalColor: null));
        Assert.Equal("Sempre aceso", now.Effect);
        Assert.Equal("#12ABEF · Brilho 4/4", now.Detail);

        var frame = X68LightSim.Frame(X68LightState.From(img, false, null, false), Caps, 0);
        Assert.Equal(Caps.Count, frame.Length);
        Assert.All(frame, c => Assert.Equal(LedColor.FromRgb(0x12ABEF), c));
    }

    [Fact]
    public void Brightness_scales_the_light_and_zero_turns_it_off()
    {
        var img = new X68Image { LightMode = X68LightMode.AlwaysOn, LightBrightness = 2 };
        img.LightColor = 0xFF0000;
        Assert.Equal(0.5, X68LightSim.Frame(X68LightState.From(img, false, null, false), Caps, 0)[0].R, 3);
        img.LightBrightness = 0;
        Assert.All(X68LightSim.Frame(X68LightState.From(img, false, null, false), Caps, 0), c => Assert.Equal(0, c.Level));
    }

    [Fact]
    public void Rainbow_wave_is_described_and_spreads_hues_across_the_board()
    {
        var img = new X68Image { LightMode = X68LightMode.Wave, LightBrightness = 4, LightSpeed = 2, LightOption = 1 };
        img.LightRainbow = true;
        var now = InPortuguese(() => X68LightNow.From(img, false, null));
        Assert.Equal("Onda · Para a esquerda", now.Effect);
        Assert.Equal("Arco-íris · Brilho 4/4 · Velocidade 2/4", now.Detail);

        var state = X68LightState.From(img, false, null, false);
        Assert.True(X68LightSim.IsAnimated(state));
        var frame = X68LightSim.Frame(state, Caps, 0);
        Assert.True(frame.Distinct().Count() > 5);
        Assert.NotEqual(frame, X68LightSim.Frame(state, Caps, 0.5));
    }

    [Fact]
    public void Lights_off_leave_every_key_dark()
    {
        var img = new X68Image { LightMode = X68LightMode.Off, LightBrightness = 4 };
        var now = InPortuguese(() => X68LightNow.From(img, false, null));
        Assert.Equal("Desligado", now.Effect);
        Assert.Equal("", now.Detail);
        var state = X68LightState.From(img, false, null, false);
        Assert.False(X68LightSim.IsAnimated(state));
        Assert.All(X68LightSim.Frame(state, Caps, 3), c => Assert.Equal(0, c.Level));
    }

    [Fact]
    public void Custom_picture_shows_each_keys_stored_colour()
    {
        var img = new X68Image { LightMode = X68LightMode.UserPicture, LightOption = 2, LightBrightness = 4 };
        var cap = Caps.First(c => c.Label == "A");
        img.UserPics[2][cap.Slot * 3] = 0x00;
        img.UserPics[2][cap.Slot * 3 + 1] = 0xFF;
        img.UserPics[2][cap.Slot * 3 + 2] = 0x00;
        var frame = X68LightSim.Frame(X68LightState.From(img, false, null, false), Caps, 0);
        var index = Caps.ToList().IndexOf(cap);
        Assert.Equal(new LedColor(0, 1, 0), frame[index]);
        Assert.Equal(0, frame[(index + 1) % Caps.Count].Level);
    }

    [Fact]
    public void SignalRgb_shows_the_streamed_colour_on_every_key()
    {
        var img = new X68Image { LightMode = X68LightMode.ScreenColor, LightBrightness = 1 };
        var waiting = InPortuguese(() => X68LightNow.From(img, true, null));
        Assert.Equal("Cor da tela (SignalRGB)", waiting.Effect);
        Assert.Equal("Aguardando cores do SignalRGB", waiting.Detail);
        Assert.All(X68LightSim.Frame(X68LightState.From(img, true, null, false), Caps, 0), c => Assert.Equal(0, c.Level));

        Assert.Equal("#0080FF", InPortuguese(() => X68LightNow.From(img, true, (0x00, 0x80, 0xFF))).Detail);
        var live = X68LightState.From(img, true, (0x00, 0x80, 0xFF), lightsDark: false);
        Assert.All(X68LightSim.Frame(live, Caps, 0), c => Assert.Equal(LedColor.FromRgb(0x0080FF), c));
        Assert.All(X68LightSim.Frame(X68LightState.From(img, true, (0x00, 0x80, 0xFF), lightsDark: true), Caps, 0), c => Assert.Equal(0, c.Level));
    }

    [Fact]
    public void An_effect_imitating_SignalRgb_is_shown_as_that_effect()
    {
        var img = new X68Image { LightMode = X68LightMode.Wave, LightBrightness = 4, LightSpeed = 3 };
        img.LightRainbow = true;
        var now = InPortuguese(() => X68LightNow.From(img, signalRgbOwns: true, signalColor: (1, 2, 3)));
        Assert.Equal("Onda · Para a direita", now.Effect);
        Assert.Equal("Imitando o SignalRGB · Arco-íris · Brilho 4/4 · Velocidade 3/4", now.Detail);
        Assert.True(X68LightSim.IsAnimated(X68LightState.From(img, true, (1, 2, 3), false)));
    }

    [Fact]
    public void Every_listed_effect_renders_finite_colours()
    {
        foreach (var info in X68Lighting.Modes)
            for (var option = 0; option < Math.Max(1, info.Options); option++)
                foreach (var rainbow in new[] { false, true })
                {
                    var img = new X68Image { LightMode = info.Mode, LightOption = option, LightBrightness = 4, LightSpeed = 4 };
                    img.LightRainbow = rainbow;
                    var state = X68LightState.From(img, false, null, false);
                    foreach (var t in new[] { 0, 0.37, 2.5, 61.2 })
                        Assert.All(X68LightSim.Frame(state, Caps, t), c =>
                        {
                            Assert.InRange(c.R, 0, 1); Assert.InRange(c.G, 0, 1); Assert.InRange(c.B, 0, 1);
                        });
                }
    }
}
