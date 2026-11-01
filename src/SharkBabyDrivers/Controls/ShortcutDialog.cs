using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SharkBabyDrivers.Core.Devices.X11;
using SharkBabyDrivers.Core.Input;
using SharkBabyDrivers.Localization;

namespace SharkBabyDrivers.Controls;

/// <summary>Small modal window that captures one key combination (e.g. Ctrl+Shift+S).</summary>
public sealed class ShortcutDialog : Window
{
    private readonly TextBlock _preview;
    private KeyModifiers _mods;
    public ButtonAction? Result { get; private set; }

    public ShortcutDialog()
    {
        Title = Loc.T("custom_shortcut");
        Width = 380; Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.FindResource("Surface");
        _preview = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Text = "…" };
        var cancel = new Button { Content = Loc.T("cancel"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        cancel.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = Loc.T("shortcut_capture"), Style = (Style)Application.Current.FindResource("Hint"), Margin = new Thickness(0, 0, 0, 14), HorizontalAlignment = HorizontalAlignment.Center },
                _preview,
                cancel,
            },
        };
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        PreviewKeyDown += OnKey;
        PreviewKeyUp += (_, e) => { _mods = CurrentMods(); _preview.Text = Describe(0); e.Handled = true; };
    }

    private static KeyModifiers CurrentMods()
    {
        var m = KeyModifiers.None;
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) m |= KeyModifiers.Ctrl;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) m |= KeyModifiers.Shift;
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) m |= KeyModifiers.Alt;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) m |= KeyModifiers.Win;
        return m;
    }

    private string Describe(byte usage) =>
        ViewModels.X11Vm.DescribeKey(new ButtonAction(ButtonAction.KeyboardCode, (byte)_mods, usage)) is { Length: > 0 } s ? s : "…";

    private void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        _mods = CurrentMods();
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            _preview.Text = Describe(0);
            return;
        }
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (HidUsage.FromVirtualKey(vk) is not { } usage) return;
        Result = ButtonAction.Key(_mods, usage);
        _preview.Text = Describe(usage);
        Dispatcher.BeginInvoke(Close, System.Windows.Threading.DispatcherPriority.Background);
    }
}

/// <summary>Dark title bar on Windows 10 20H1+ / 11.</summary>
public static class DarkTitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Apply(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        var on = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        // Windows 11: match the caption color to the sidebar.
        var caption = 0x00121116; // BGR of #161112 (sidebar)
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
    }
}
