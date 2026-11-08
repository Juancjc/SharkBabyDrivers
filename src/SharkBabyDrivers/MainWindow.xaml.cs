using System.Windows;
using System.Windows.Interop;
using SharkBabyDrivers.Controls;
using SharkBabyDrivers.ViewModels;

namespace SharkBabyDrivers;

public partial class MainWindow : Window
{
    private const int WmDeviceChange = 0x0219;
    private readonly MainVm _vm;
    private WindowState _restoreState = WindowState.Normal;

    public MainVm Vm => _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm = new MainVm();
        ((App)Application.Current).Updater.StateChanged += UpdateBannerState;
        Localization.Loc.I.PropertyChanged += UpdateBannerLanguage;
        Closed += (_, _) =>
        {
            ((App)Application.Current).Updater.StateChanged -= UpdateBannerState;
            Localization.Loc.I.PropertyChanged -= UpdateBannerLanguage;
        };
        SourceInitialized += (_, _) =>
        {
            DarkTitleBar.Apply(this);
            var handle = new WindowInteropHelper(this).Handle;
            // Key presses of the Attack Shark keyboard bring its lights back after the idle light-off.
            if (!App.IsPreview) Services.KeyboardActivity.Register(handle);
            // Plug/unplug notifications trigger a rescan right away (a slow poll is the fallback).
            HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == WmDeviceChange) _vm.RequestRefresh();
                else if (!App.IsPreview && msg == Services.KeyboardActivity.WmInput && Services.KeyboardActivity.IsAttackSharkKeyDown(l))
                    _vm.Lighting.NotifyKeyActivity();
                return IntPtr.Zero;
            });
        };
        IsVisibleChanged += (_, _) => _vm.WindowVisible = IsVisible;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) Hide();
            else _restoreState = WindowState;
        };
        Closing += (_, e) =>
        {
            if (App.ExitRequested) return;
            // The close button only hides the window: the tray icon keeps the keyboard's lighting running.
            e.Cancel = true;
            Hide();
        };
        Closed += (_, _) => _vm.Dispose();
    }

    /// <summary>Creates the window handle (device notifications need it) without showing anything.</summary>
    public void PrepareHidden() => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>Returns from the tray without losing a maximized window's previous size.</summary>
    public void RestoreFromTray()
    {
        if (WindowState == WindowState.Minimized) WindowState = _restoreState;
        Show();
        Activate();
        ((App)Application.Current).Updater.ShowNews();
    }

    private void UpdateBannerLanguage(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateBannerState();

    private void UpdateBannerState()
    {
        var updater = ((App)Application.Current).Updater;
        UpdateBanner.Visibility = updater.AvailableRelease is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateBanner.IsEnabled = !updater.IsBusy;
        var version = updater.AvailableRelease?.Version;
        UpdateBanner.Content = Localization.Loc.I.Language == "en"
            ? $"\u26A0 Update available - v{version}. Click to update."
            : $"\u26A0 Atualiza\u00E7\u00E3o dispon\u00EDvel - v{version}. Clique para atualizar.";
    }

    private async void UpdateBannerClick(object sender, RoutedEventArgs e) =>
        await ((App)Application.Current).Updater.CheckAsync(manual: true, install: true);

    public void MinimizeToTray() => WindowState = WindowState.Minimized;

    /// <summary>Asks about unsaved edits before the app really exits.</summary>
    public bool ConfirmLeave()
    {
        if (!_vm.Devices.Any(d => d.IsDirty)) return true;
        RestoreFromTray();
        return App.Confirm(Localization.Loc.T("pending_changes") + " — " + Localization.Loc.T("discard") + "?");
    }
}
