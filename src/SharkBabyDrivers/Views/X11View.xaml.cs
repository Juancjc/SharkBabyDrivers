using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SharkBabyDrivers.Controls;
using SharkBabyDrivers.Core.Input;
using SharkBabyDrivers.Localization;
using SharkBabyDrivers.ViewModels;

namespace SharkBabyDrivers.Views;

public partial class X11View : UserControl
{
    private readonly Stopwatch _sinceLast = new();
    private readonly HashSet<byte> _down = new();
    private bool _updatingActionPicker;

    public X11View()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is X11Vm vm)
                vm.CaptureShortcut = initial =>
                {
                    var dlg = new ShortcutDialog(initial) { Owner = Window.GetWindow(this) };
                    dlg.ShowDialog();
                    return dlg.Result;
                };
        };
        PreviewKeyDown += (_, e) => Record(e, true);
        PreviewKeyUp += (_, e) => Record(e, false);
    }

    private X11Vm? Vm => DataContext as X11Vm;

    private void ActionPicker_DropDownOpened(object? sender, EventArgs e)
    {
        if (sender is ComboBox { DataContext: ButtonVm button } picker)
        {
            _updatingActionPicker = true;
            try { picker.SetCurrentValue(ComboBox.SelectedItemProperty, button.Selected); }
            finally { _updatingActionPicker = false; }
        }
    }

    private void ActionPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { IsDropDownOpen: true }) CommitActionPicker(sender);
    }

    private void ActionPicker_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is ComboBox { IsDropDownOpen: false }) ActionPicker_DropDownOpened(sender, EventArgs.Empty);
    }

    private void ActionPicker_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (sender is ComboBox { IsDropDownOpen: false } && e.Key != Key.Escape)
            CommitActionPicker(sender);
    }

    private void CommitActionPicker(object? sender)
    {
        if (_updatingActionPicker) return;
        if (sender is ComboBox { DataContext: ButtonVm button, SelectedItem: ActionOption option })
        {
            _updatingActionPicker = true;
            try { button.Selected = option; }
            finally { _updatingActionPicker = false; }
        }
    }

    private void Record(KeyEventArgs e, bool down)
    {
        if (Vm is not { Recording: true, SelectedMacro: { } macro }) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (HidUsage.FromVirtualKey(vk) is not { } usage) return;
        if (down && !_down.Add(usage)) return; // ignore auto-repeat
        if (!down) _down.Remove(usage);
        // The delay belongs to the previous step (time until this event).
        var delay = _sinceLast.IsRunning ? (int)Math.Clamp(_sinceLast.ElapsedMilliseconds, 10, 60000) : 30;
        if (macro.Steps.Count > 0) macro.Steps[^1].DelayMs = delay;
        macro.Add(usage, down, 30);
        _sinceLast.Restart();
        Vm.Changed();
    }

    private void Record_Checked(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.Recording = true;
        _down.Clear();
        _sinceLast.Reset();
        RecordText.Text = Loc.T("macro_stop");
        Focus();
        Keyboard.Focus(this);
    }

    private void Record_Unchecked(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.Recording = false;
        RecordText.Text = Loc.T("macro_record");
    }

    private void MacroName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedMacro is { } m && sender is TextBox t) Vm.RenameMacro(m, t.Text);
    }
}

/// <summary>True when both bound values are equal (used for choice buttons bound to a value + the current selection).</summary>
public sealed class MultiEquals : IMultiValueConverter
{
    public static readonly MultiEquals Instance = new();
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && Equals(values[0], values[1]);
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
