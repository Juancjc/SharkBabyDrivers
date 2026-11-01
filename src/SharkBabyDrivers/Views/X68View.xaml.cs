using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SharkBabyDrivers.Core.Input;
using SharkBabyDrivers.Localization;
using SharkBabyDrivers.ViewModels;

namespace SharkBabyDrivers.Views;

public partial class X68View : UserControl
{
    private readonly Stopwatch _sinceLast = new();
    private readonly HashSet<byte> _down = new();
    private TextBlock? _recordText;

    public X68View()
    {
        InitializeComponent();
        // Ctrl + click on a key cap adds it to the selection instead of replacing it.
        PreviewMouseLeftButtonDown += (_, _) => { if (Vm is { } vm) vm.MultiSelect = Keyboard.Modifiers.HasFlag(ModifierKeys.Control); };
        PreviewKeyDown += (_, e) => Record(e, true);
        PreviewKeyUp += (_, e) => Record(e, false);
    }

    private X68Vm? Vm => DataContext as X68Vm;

    private void Record(KeyEventArgs e, bool down)
    {
        if (Vm is not { Recording: true, SelectedMacro: { } macro }) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HidUsage.FromVirtualKey(KeyInterop.VirtualKeyFromKey(key)) is not { } usage) return;
        if (down && !_down.Add(usage)) return;
        if (!down) _down.Remove(usage);
        var delay = _sinceLast.IsRunning ? (int)Math.Clamp(_sinceLast.ElapsedMilliseconds, 10, 65535) : 30;
        if (macro.Steps.Count > 0) macro.Steps[^1].DelayMs = delay;
        Vm.AddMacroStep(usage, down, 30);
        _sinceLast.Restart();
    }

    private void Record_Checked(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.Recording = true;
        _down.Clear();
        _sinceLast.Reset();
        _recordText = (sender as FrameworkElement)?.FindName("RecordText") as TextBlock ?? FindText(sender);
        if (_recordText is not null) _recordText.Text = Loc.T("macro_stop");
        Keyboard.Focus(this);
    }

    private void Record_Unchecked(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.Recording = false;
        if (_recordText is not null) _recordText.Text = Loc.T("macro_record");
    }

    private static TextBlock? FindText(object sender) =>
        sender is ContentControl { Content: Panel p } ? p.Children.OfType<TextBlock>().FirstOrDefault() : null;

    private void Repeat_LostFocus(object sender, RoutedEventArgs e) => Vm?.SyncMacro();
}

/// <summary>Shows a 0-based index as 1-based.</summary>
public sealed class PlusOne : IValueConverter
{
    public static readonly PlusOne Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int i ? i + 1 : value!;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        int.TryParse(value?.ToString(), out var i) && i >= 1 ? i - 1 : Binding.DoNothing;
}
