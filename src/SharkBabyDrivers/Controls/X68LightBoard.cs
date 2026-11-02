using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SharkBabyDrivers.Core.Devices.X68;
using SharkBabyDrivers.ViewModels;

namespace SharkBabyDrivers.Controls;

/// <summary>
/// The keyboard drawn with its backlight on: same caps and legends as the Keys tab, lit with the effect stored on the
/// keyboard. Animated effects redraw at ~30 fps only while the drawing is on screen.
/// </summary>
public sealed class X68LightBoard : FrameworkElement
{
    private const double Unit = KeyCapVm.Unit;
    private const double Pad = 14;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(X68Model), typeof(X68LightBoard),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((X68LightBoard)d).OnModelChanged()));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(X68LightState), typeof(X68LightBoard),
        new FrameworkPropertyMetadata(X68LightState.Off, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((X68LightBoard)d).UpdateAnimation()));

    public X68Model? Model { get => (X68Model?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public X68LightState State { get => (X68LightState)GetValue(StateProperty); set => SetValue(StateProperty, value); }

    private FormattedText?[] _legends = [];
    private bool _animating;
    private TimeSpan _lastFrame;

    public X68LightBoard()
    {
        Loaded += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => StopAnimation();
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    private void OnModelChanged()
    {
        _legends = new FormattedText?[Model?.Caps.Count ?? 0];
        UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        var want = IsLoaded && IsVisible && Model is not null && State is { } s && X68LightSim.IsAnimated(s);
        if (want == _animating) return;
        _animating = want;
        if (want) CompositionTarget.Rendering += OnRendering;
        else CompositionTarget.Rendering -= OnRendering;
    }

    private void StopAnimation()
    {
        if (!_animating) return;
        _animating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = Clock.Elapsed;
        if (now - _lastFrame < TimeSpan.FromMilliseconds(33)) return;
        _lastFrame = now;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => Model is { } m
        ? new Size(m.Width * Unit + Pad * 2, m.Height * Unit + Pad * 2)
        : new Size(0, 0);

    protected override void OnRender(DrawingContext dc)
    {
        if (Model is not { } model) return;
        var board = new Rect(0, 0, model.Width * Unit + Pad * 2, model.Height * Unit + Pad * 2);
        dc.DrawRoundedRectangle(Res("Sidebar", Colors.Black), new Pen(Res("Border", Colors.Gray), 1), board, 14, 14);

        var caps = model.Caps;
        var lights = X68LightSim.Frame(State ?? X68LightState.Off, caps, Clock.Elapsed.TotalSeconds);
        var capFill = Res("Surface2", Color.FromRgb(0x1C, 0x20, 0x28));
        var capEdge = ResColor("Border", Color.FromRgb(0x2A, 0x2E, 0x38));
        var muted = ResColor("Muted", Color.FromRgb(0x9B, 0xA3, 0xB2));
        var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Light bleeding around the caps first, so every cap sits on top of its neighbours' glow.
        for (var i = 0; i < caps.Count; i++)
        {
            var level = lights[i].Level;
            if (level < 0.02) continue;
            dc.DrawRoundedRectangle(Brush(Full(lights[i]), level * 0.35), null, Inflate(CapRect(caps[i]), 3), 9, 9);
        }

        for (var i = 0; i < caps.Count; i++)
        {
            var rect = CapRect(caps[i]);
            var light = lights[i];
            var level = light.Level;
            var hue = Full(light);
            dc.DrawRoundedRectangle(capFill, new Pen(new SolidColorBrush(Mix(capEdge, hue, level * 0.9)), 1), rect, 7, 7);
            if (level >= 0.02) dc.DrawRoundedRectangle(Brush(hue, level * 0.28), null, Inflate(rect, -1), 6, 6);

            var legend = _legends[i] ??= new FormattedText(caps[i].Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                11.5, Brushes.White, dip) { MaxTextWidth = Math.Max(1, rect.Width - 10), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            legend.SetForegroundBrush(new SolidColorBrush(Mix(muted, Mix(hue, Colors.White, 0.35), level)));
            dc.DrawText(legend, new Point(rect.X + 5, rect.Y + 3));
        }
    }

    private static Rect CapRect(KeyCap cap) => new(Pad + cap.X * Unit, Pad + cap.Y * Unit, cap.Width * Unit - 4, Unit - 4);
    private static Rect Inflate(Rect r, double by) { r.Inflate(by, by); return r; }

    /// <summary>The light's colour at full intensity (its hue), white when it is off.</summary>
    private static Color Full(LedColor c)
    {
        var level = c.Level;
        if (level <= 0) return Colors.White;
        return Color.FromRgb(Byte(c.R / level), Byte(c.G / level), Byte(c.B / level));
    }

    private static Color Mix(Color a, Color b, double k)
    {
        k = Math.Clamp(k, 0, 1);
        return Color.FromRgb(Byte((a.R + (b.R - a.R) * k) / 255.0), Byte((a.G + (b.G - a.G) * k) / 255.0), Byte((a.B + (b.B - a.B) * k) / 255.0));
    }

    private static SolidColorBrush Brush(Color c, double alpha)
    {
        var b = new SolidColorBrush(Color.FromArgb(Byte(alpha), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    private static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    private Brush Res(string key, Color fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
    private Color ResColor(string key, Color fallback) => TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;
}
