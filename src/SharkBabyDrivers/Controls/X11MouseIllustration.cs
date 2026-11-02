using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using SharkBabyDrivers.Core.Devices.X11;
using SharkBabyDrivers.Localization;
using ShapePath = System.Windows.Shapes.Path;

namespace SharkBabyDrivers.Controls;

/// <summary>A scalable, native mouse diagram. Numbered buttons use the same editor as the button list.</summary>
public sealed class X11MouseIllustration : UserControl
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(X11MouseIllustration));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public X11MouseIllustration()
    {
        Focusable = false;
        var canvas = new Canvas { Width = 260, Height = 480, Focusable = false };

        // The outer shell, split click panels and restrained shading remain crisp at any window size.
        var body = DrawPath(
            "M 128,47 C 101,47 59,54 49,75 C 42,100 46,159 47,199 " +
            "C 49,246 34,296 39,342 C 43,393 78,428 130,430 " +
            "C 182,428 217,393 221,342 C 226,296 211,246 213,199 " +
            "C 214,159 218,100 211,75 C 201,54 159,47 132,47 Z",
            new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(HexColor("#FFFFFF"), 0),
                    new(HexColor("#ECEFF3"), 0.48),
                    new(HexColor("#CED4DC"), 1),
                }, new Point(0.15, 0), new Point(0.85, 1)),
            "#AAB2BE", 1);
        body.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.28, BlurRadius = 22, ShadowDepth = 10, Direction = 270 };
        canvas.Children.Add(body);

        // A thin dark inner frame shows through at the edge of the two click panels.
        canvas.Children.Add(DrawPath(
            "M 51,76 C 45,105 49,167 50,207 M 209,76 C 215,105 211,167 210,207",
            null, "#68727F", 2));
        canvas.Children.Add(DrawPath(
            "M 51,76 C 53,63 91,47 122,32 L 122,174 C 122,187 119,200 115,208 " +
            "L 49,210 C 48,172 44,105 51,76 Z",
            new LinearGradientBrush(HexColor("#FFFFFF"), HexColor("#E4E8ED"), new Point(0, 0), new Point(1, 1)),
            "#C8CFD8", 0.7));
        canvas.Children.Add(DrawPath(
            "M 209,76 C 207,63 169,47 138,32 L 138,174 C 138,187 141,200 145,208 " +
            "L 211,210 C 212,172 216,105 209,76 Z",
            new LinearGradientBrush(HexColor("#FFFFFF"), HexColor("#E5E9EE"), new Point(0, 0), new Point(0.75, 1)),
            "#C8CFD8", 0.7));
        canvas.Children.Add(DrawPath("M 130,66 L 130,207 M 49,210 Q 130,204 211,210", null, "#828C99", 1));

        // Wheel with individual rubber ribs, rather than a bitmap.
        Add(canvas, new Border
        {
            Width = 29, Height = 68, CornerRadius = new CornerRadius(11),
            Background = Brush("#C3CBD4"), BorderBrush = Brush("#8D98A5"), BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        }, 115.5, 76);
        Add(canvas, new Border
        {
            Width = 21, Height = 60, CornerRadius = new CornerRadius(8),
            Background = new LinearGradientBrush(HexColor("#080C10"), HexColor("#414A54"), new Point(0, 0), new Point(1, 0)),
            IsHitTestVisible = false,
        }, 119.5, 80);
        for (var y = 85; y <= 135; y += 5)
            Add(canvas, new Border { Width = 15, Height = 1, Background = Brush("#68717C"), Opacity = 0.65, IsHitTestVisible = false }, 122.5, y);

        // Two side buttons on the left edge.
        canvas.Children.Add(DrawPath("M 44,169 Q 39,173 39,184 L 39,211 Q 40,215 45,213 Z", Brush("#5D6875"), "#AEB8C3", 1));
        canvas.Children.Add(DrawPath("M 43,223 Q 37,226 37,238 L 37,264 Q 38,269 43,266 Z", Brush("#5D6875"), "#AEB8C3", 1));

        Add(canvas, new Border
        {
            Width = 12, Height = 3, CornerRadius = new CornerRadius(2), Background = Brush("#50DDDC"),
            Effect = new DropShadowEffect { Color = HexColor("#50DDDC"), BlurRadius = 5, ShadowDepth = 0, Opacity = 0.45 },
            IsHitTestVisible = false,
        }, 124, 219);

        var hotspotStyle = HotspotStyle();
        Hotspot(canvas, hotspotStyle, X11Button.Left, "1", "btn_left", 71, 101);
        Hotspot(canvas, hotspotStyle, X11Button.Right, "2", "btn_right", 159, 101);
        Hotspot(canvas, hotspotStyle, X11Button.Middle, "3", "btn_middle", 115, 92);
        Hotspot(canvas, hotspotStyle, X11Button.Forward, "4", "btn_forward", 23, 176);
        Hotspot(canvas, hotspotStyle, X11Button.Backward, "5", "btn_backward", 21, 234);

        // The DPI button is called out separately from the five buttons visible in the top view.
        canvas.Children.Add(DrawPath("M 130,439 L 130,447 L 157,447", null, "#596574", 1));
        Hotspot(canvas, hotspotStyle, X11Button.Dpi, "6", "btn_dpi", 158, 432);
        Add(canvas, new TextBlock { Text = "DPI", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brush("#9BA3B2"), IsHitTestVisible = false }, 194, 442);

        Content = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = canvas };
    }

    private void Hotspot(Canvas canvas, Style style, X11Button mouseButton, string number, string labelKey, double x, double y)
    {
        var button = new Button
        {
            Name = "Mouse" + mouseButton, Content = number, Style = style,
            Width = 30, Height = 30, CommandParameter = mouseButton,
        };
        button.SetBinding(Button.CommandProperty, new Binding(nameof(Command)) { Source = this });
        button.SetBinding(ToolTipProperty, new Binding($"[{labelKey}]") { Source = Loc.I });
        button.SetBinding(AutomationProperties.NameProperty, new Binding($"[{labelKey}]") { Source = Loc.I });
        Add(canvas, button, x, y);
    }

    private static Style HotspotStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(Content)) { RelativeSource = RelativeSource.TemplatedParent });
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(BackgroundProperty, Brush("#F04452")));
        style.Setters.Add(new Setter(BorderBrushProperty, Brush("#FF8790")));
        style.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(FontWeightProperty, FontWeights.Bold));
        style.Setters.Add(new Setter(CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(MinHeightProperty, 0.0));
        style.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = border }));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BackgroundProperty, Brush("#FF6270")));
        hover.Setters.Add(new Setter(BorderBrushProperty, Brushes.White));
        style.Triggers.Add(hover);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(BorderBrushProperty, Brushes.White));
        style.Triggers.Add(focus);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(BackgroundProperty, Brush("#D92E40")));
        style.Triggers.Add(pressed);
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(OpacityProperty, 0.45));
        style.Triggers.Add(disabled);
        return style;
    }

    private static ShapePath DrawPath(string data, Brush? fill, string stroke, double thickness) => new()
    {
        Data = Geometry.Parse(data), Fill = fill, Stroke = Brush(stroke), StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false,
    };

    private static void Add(Canvas canvas, FrameworkElement child, double x, double y)
    {
        Canvas.SetLeft(child, x);
        Canvas.SetTop(child, y);
        canvas.Children.Add(child);
    }

    private static Color HexColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static Brush Brush(string hex) => new SolidColorBrush(HexColor(hex));
}
