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

/// <summary>Edits a single key or shortcut without committing until the user confirms.</summary>
public sealed class ShortcutDialog : Window
{
    private readonly TextBlock _preview;
    private readonly ComboBox _key;
    private readonly Dictionary<KeyModifiers, CheckBox> _modifiers = new();
    private readonly HashSet<Key> _captureDown = new();
    private readonly Button _capture;
    private readonly Button _confirm;
    private bool _capturing;
    private KeyModifiers _capturedMods;
    public ButtonAction? Result { get; private set; }

    private sealed record KeyChoice(byte Usage, string Label);

    public ShortcutDialog(ButtonAction? initial = null)
    {
        Title = Loc.T("shortcut_title");
        Width = 500;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.FindResource("Surface");

        _preview = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        _key = new ComboBox { DisplayMemberPath = nameof(KeyChoice.Label), Margin = new Thickness(0, 6, 0, 16) };
        System.Windows.Automation.AutomationProperties.SetName(_key, Loc.T("shortcut_key"));
        _key.Items.Add(new KeyChoice(0, Loc.T("shortcut_no_key")));
        foreach (var (usage, name) in HidUsage.KeyNames.Where(k => !HidUsage.IsModifier(k.Key)).OrderBy(k => k.Key))
            _key.Items.Add(new KeyChoice(usage, name));

        var modifiers = new WrapPanel { Margin = new Thickness(0, 8, 0, 18) };
        foreach (var modifier in new[] { KeyModifiers.Ctrl, KeyModifiers.Shift, KeyModifiers.Alt, KeyModifiers.Win })
        {
            var check = new CheckBox { Content = modifier.ToString(), Margin = new Thickness(0, 0, 18, 0) };
            _modifiers.Add(modifier, check);
            modifiers.Children.Add(check);
        }

        _capture = new Button { Content = Loc.T("shortcut_capture_start"), Margin = new Thickness(0, 14, 0, 8) };
        _capture.Click += (_, _) =>
        {
            if (_capturing) StopCapture();
            else
            {
                _capturing = true;
                _capturedMods = KeyModifiers.None;
                UpdatePreview();
                _capture.Focus();
            }
        };
        _confirm = new Button { Content = Loc.T("confirm"), Style = (Style)Application.Current.FindResource("Primary"), MinWidth = 110, IsDefault = true };
        _confirm.Click += (_, _) =>
        {
            if (_capturing || SelectedAction is not { } action) return;
            Result = action;
            DialogResult = true;
        };
        var cancel = new Button { Content = Loc.T("cancel"), IsCancel = true, MinWidth = 100, Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = Loc.T("shortcut_title"), Style = (Style)Application.Current.FindResource("H2") },
                new TextBlock { Text = Loc.T("shortcut_hint"), Style = (Style)Application.Current.FindResource("Hint"), Margin = new Thickness(0, 0, 0, 16) },
                new TextBlock { Text = Loc.T("shortcut_key"), Style = (Style)Application.Current.FindResource("Label") },
                _key,
                new TextBlock { Text = Loc.T("shortcut_modifiers"), Style = (Style)Application.Current.FindResource("Label") },
                modifiers,
                new Border
                {
                    Background = (Brush)Application.Current.FindResource("Surface2"), CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(16), MinHeight = 64, Child = _preview,
                },
                _capture,
                new TextBlock { Text = Loc.T("shortcut_apply_hint"), Style = (Style)Application.Current.FindResource("Hint") },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 20, 0, 0), Children = { cancel, _confirm },
                },
            },
        };

        if (initial is { IsKeyboard: true } initialAction)
        {
            // Keep an imported key even if it is not in the named key list.
            if (!_key.Items.Cast<KeyChoice>().Any(k => k.Usage == initialAction.P2))
                _key.Items.Add(new KeyChoice(initialAction.P2, HidUsage.KeyName(initialAction.P2)));
            SetSelection((KeyModifiers)initialAction.P1, initialAction.P2);
        }
        else _key.SelectedIndex = 0;
        _key.SelectionChanged += (_, _) => UpdatePreview();
        foreach (var check in _modifiers.Values)
        {
            check.Checked += (_, _) => UpdatePreview();
            check.Unchecked += (_, _) => UpdatePreview();
        }
        UpdatePreview();

        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        PreviewKeyDown += OnKey;
        PreviewKeyUp += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var captured = _captureDown.Remove(key);
            if (!_capturing && !captured) return;
            e.Handled = true;
            if (_capturedMods != KeyModifiers.None && CurrentMods() == KeyModifiers.None) StopCapture();
        };
        Deactivated += (_, _) => { StopCapture(); _captureDown.Clear(); };
    }

    private ButtonAction? SelectedAction
    {
        get
        {
            var mods = KeyModifiers.None;
            foreach (var (modifier, check) in _modifiers)
                if (check.IsChecked == true) mods |= modifier;
            var usage = (_key.SelectedItem as KeyChoice)?.Usage ?? 0;
            return mods == KeyModifiers.None && usage == 0 ? null : ButtonAction.Key(mods, usage);
        }
    }

    private void SetSelection(KeyModifiers mods, byte usage)
    {
        foreach (var (modifier, check) in _modifiers) check.IsChecked = mods.HasFlag(modifier);
        _key.SelectedItem = _key.Items.Cast<KeyChoice>().First(k => k.Usage == usage);
    }

    private void UpdatePreview()
    {
        _preview.Text = _capturing ? Loc.T("shortcut_capture")
            : SelectedAction is { } action ? ViewModels.X11Vm.DescribeKey(action) : Loc.T("shortcut_empty");
        _confirm.IsEnabled = !_capturing && SelectedAction is not null;
        _capture.Content = Loc.T(_capturing ? "shortcut_capture_stop" : "shortcut_capture_start");
        _key.IsEnabled = !_capturing;
        foreach (var check in _modifiers.Values) check.IsEnabled = !_capturing;
    }

    private void StopCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        UpdatePreview();
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

    private void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!_capturing)
        {
            // A held Space/Enter must not activate buttons after its capture ends.
            if (_captureDown.Contains(key)) e.Handled = true;
            return;
        }
        e.Handled = true;
        _captureDown.Add(key);
        if (e.IsRepeat) return;
        _capturedMods |= CurrentMods();
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            SetSelection(_capturedMods, 0);
            _preview.Text = ViewModels.X11Vm.DescribeKey(ButtonAction.Key(_capturedMods, 0));
            return;
        }
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (HidUsage.FromVirtualKey(vk) is not { } usage) return;
        SetSelection(CurrentMods(), usage);
        StopCapture();
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
