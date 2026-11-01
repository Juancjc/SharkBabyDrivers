using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SharkBabyDrivers.Controls;

/// <summary>A swatch that opens a small palette with a hex field. Color is "#RRGGBB".</summary>
public sealed class ColorButton : Button
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(string), typeof(ColorButton),
        new FrameworkPropertyMetadata("#FFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ColorButton)d).UpdateSwatch()));

    private static readonly string[] Palette =
    [
        "#FF0000", "#FF5500", "#FF8000", "#FFC000", "#FFFF00", "#80FF00", "#00FF00", "#00FF80",
        "#00FFFF", "#0080FF", "#0000FF", "#5500FF", "#8000FF", "#FF00FF", "#FF0080", "#FFFFFF",
    ];

    private readonly Border _swatch;
    private readonly Popup _popup;
    private readonly TextBox _hex;

    public string Color { get => (string)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    public ColorButton()
    {
        Style = (Style)Application.Current.FindResource("BaseButton");
        Padding = new Thickness(4);
        _swatch = new Border { Width = 26, Height = 18, CornerRadius = new CornerRadius(4) };
        Content = _swatch;

        var grid = new WrapPanel { Width = 8 * 30, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var c in Palette)
        {
            var b = new Button
            {
                Width = 26, Height = 26, Margin = new Thickness(2), Padding = new Thickness(0), Tag = c,
                Style = (Style)Application.Current.FindResource("BaseButton"),
                Content = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(3), Background = Brush(c) },
            };
            b.Click += (_, _) => { Color = c; _popup!.IsOpen = false; };
            grid.Children.Add(b);
        }
        _hex = new TextBox { Width = 110 };
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitHex(); };
        var ok = new Button { Content = "OK", Margin = new Thickness(6, 0, 0, 0) };
        ok.Click += (_, _) => CommitHex();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { _hex, ok } };
        _popup = new Popup
        {
            PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Border
            {
                Background = (Brush)Application.Current.FindResource("Surface2"),
                BorderBrush = (Brush)Application.Current.FindResource("Border"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10),
                Margin = new Thickness(0, 4, 0, 0),
                Child = new StackPanel { Children = { grid, row } },
            },
        };
        Click += (_, _) => { _hex.Text = Color; _popup.IsOpen = true; };
        UpdateSwatch();
    }

    private void CommitHex()
    {
        var t = _hex.Text.Trim();
        if (!t.StartsWith('#')) t = "#" + t;
        if (t.Length == 7 && int.TryParse(t[1..], System.Globalization.NumberStyles.HexNumber, null, out _))
        {
            Color = t.ToUpperInvariant();
            _popup.IsOpen = false;
        }
    }

    private void UpdateSwatch() => _swatch.Background = Brush(Color);

    private static Brush Brush(string hex)
    {
        try { return new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex)); }
        catch (FormatException) { return Brushes.Transparent; }
    }
}
