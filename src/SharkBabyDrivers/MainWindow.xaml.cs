using System.Windows;
using System.Windows.Interop;
using SharkBabyDrivers.Controls;
using SharkBabyDrivers.ViewModels;

namespace SharkBabyDrivers;

public partial class MainWindow : Window
{
    private const int WmDeviceChange = 0x0219;
    private readonly MainVm _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm = new MainVm();
        SourceInitialized += (_, _) =>
        {
            DarkTitleBar.Apply(this);
            // Plug/unplug notifications trigger a rescan right away (a slow poll is the fallback).
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == WmDeviceChange) _vm.RequestRefresh();
                return IntPtr.Zero;
            });
        };
        Closing += (_, e) =>
        {
            if (_vm.Devices.Any(d => d.IsDirty) && !App.Confirm(Localization.Loc.T("pending_changes") + " — " + Localization.Loc.T("discard") + "?"))
                e.Cancel = true;
        };
        Closed += (_, _) => _vm.Dispose();
    }
}
