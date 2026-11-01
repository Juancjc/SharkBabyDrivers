using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using SharkBabyDrivers.Core;
using SharkBabyDrivers.Core.Devices;
using SharkBabyDrivers.Core.Devices.X11;
using SharkBabyDrivers.Core.Devices.X68;
using SharkBabyDrivers.Core.Storage;
using SharkBabyDrivers.Infrastructure;
using SharkBabyDrivers.Localization;

namespace SharkBabyDrivers.ViewModels;

public sealed class MainVm : ObservableObject, IDisposable
{
    private readonly DeviceManager _manager = new(DeviceCatalog.Drivers, DeviceCatalog.VendorIds);
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly AppSettings _settings = AppSettings.Load();
    private DeviceVm? _selected;
    private bool _refreshing;

    public ObservableCollection<DeviceVm> Devices { get; } = new();
    public IReadOnlyList<Choice<string>> Languages { get; } = Loc.Languages.Select(l => new Choice<string>(l.Code, "", () => l.Name)).ToList();
    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenBackupsCommand { get; }
    public string Version => App.Version;

    public MainVm()
    {
        Loc.I.Language = _settings.Language;
        _manager.Connected += d => Ui.Post(() => OnConnected(d));
        _manager.Disconnected += d => Ui.Post(() => OnDisconnected(d));
        _manager.OpenFailed += (model, ex) => App.Log(new IOException($"{model}: {ex.Message}", ex));
        RefreshCommand = new RelayCommand(RequestRefresh);
        OpenBackupsCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(AppPaths.Backups);
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Backups) { UseShellExecute = true });
        });
        _debounce.Tick += (_, _) => { _debounce.Stop(); Refresh(); };
        _poll.Tick += (_, _) => Refresh();
        _poll.Start();
        Refresh();
        if (Environment.GetCommandLineArgs().Contains("--demo", StringComparer.OrdinalIgnoreCase))
        {
            // A simulated X68 PRO HE to explore the keyboard pages without the cable (nothing touches real hardware).
            var sim = new X68Simulator(X68Simulator.FactoryImage());
            OnConnected(new X68Device(sim, "x68-demo") { TransferDelayMs = 0, WriteSettleMs = 0 });
        }
    }

    public DeviceVm? Selected { get => _selected; set { Set(ref _selected, value); OnPropertyChanged(nameof(HasDevices)); } }
    public bool HasDevices => Devices.Count > 0;

    public string Language
    {
        get => Loc.I.Language;
        set
        {
            Loc.I.Language = value;
            _settings.Language = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Called on WM_DEVICECHANGE; bursts of notifications collapse into one rescan.</summary>
    public void RequestRefresh()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async void Refresh()
    {
        if (_refreshing) return;
        // Never rescan while a page is talking to its device.
        if (Devices.Any(d => d.Busy)) return;
        _refreshing = true;
        try { await Task.Run(_manager.Refresh); }
        catch (Exception ex) { App.Log(ex); }
        finally { _refreshing = false; }
    }

    private void OnConnected(ISharkDevice device)
    {
        DeviceVm vm = device switch
        {
            X11Device m => new X11Vm(m),
            X68Device k => new X68Vm(k),
            _ => new UnsupportedDeviceVm(device),
        };
        Devices.Add(vm);
        OnPropertyChanged(nameof(HasDevices));
        Selected ??= vm;
        _ = vm.InitializeAsync();
    }

    private void OnDisconnected(ISharkDevice device)
    {
        var vm = Devices.FirstOrDefault(d => d.Device == device);
        if (vm is null) return;
        Devices.Remove(vm);
        vm.Dispose();
        if (Selected == vm) Selected = Devices.FirstOrDefault();
        OnPropertyChanged(nameof(HasDevices));
    }

    public void Dispose()
    {
        _poll.Stop();
        _debounce.Stop();
        foreach (var d in Devices) d.Dispose();
        _manager.Dispose();
    }
}
