using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using SharkBabyDrivers.Core.Devices;
using SharkBabyDrivers.Core.Storage;
using SharkBabyDrivers.Infrastructure;
using SharkBabyDrivers.Localization;

namespace SharkBabyDrivers.ViewModels;

public enum DeviceState { Reading, WaitingWake, Ready, Error, Unsupported }

public sealed class BackupItemVm(BackupEntry entry)
{
    public BackupEntry Entry { get; } = entry;
    public string When => Entry.Snapshot.CreatedAt.LocalDateTime.ToString("g");
    public bool IsOriginal => Entry.IsOriginal;
    public string Summary => Entry.Snapshot.Notes.GetValueOrDefault("summary") ?? Entry.Snapshot.Firmware;
}

/// <summary>
/// Base for every device page. Enforces the rule of the app: the configuration stored inside the device is
/// read and backed up BEFORE anything can be written to it.
/// </summary>
public abstract class DeviceVm : ObservableObject, IDisposable
{
    protected readonly BackupStore Backups = new();
    private DeviceState _state = DeviceState.Reading;
    private string _statusText = "";
    private bool _busy;
    private bool _dirty;
    private string? _toast;
    private CancellationTokenSource? _toastCts;

    public ISharkDevice Device { get; }
    public ObservableCollection<BackupItemVm> BackupList { get; } = new();

    protected DeviceVm(ISharkDevice device)
    {
        Device = device;
        ApplyCommand = new AsyncCommand(ApplyAsync, () => CanWrite && IsDirty);
        DiscardCommand = new RelayCommand(Discard, () => IsDirty && !Busy);
        RetryCommand = new AsyncCommand(InitializeAsync, () => !Busy);
        BackupNowCommand = new AsyncCommand(BackupNowAsync, () => State == DeviceState.Ready && !Busy);
        RestoreCommand = new AsyncCommand(p => RestoreAsync(((BackupItemVm)p!).Entry.Snapshot), p => p is BackupItemVm && CanWrite);
        ExportBackupCommand = new RelayCommand(p => ExportBackup((BackupItemVm)p!), p => p is BackupItemVm);
        ImportBackupCommand = new AsyncCommand(ImportBackupAsync, () => CanWrite);
        OpenBackupFolderCommand = new RelayCommand(() =>
        {
            var folder = Backups.Folder(Device.DeviceKey);
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        });
        Loc.I.PropertyChanged += (_, _) => OnLanguageChanged();
    }

    public string Title => Device.DeviceKey.Contains("demo", StringComparison.Ordinal) ? $"{Device.Model} (demo)" : Device.Model;
    public string KindText => Loc.T(Device.Kind == DeviceKind.Mouse ? "kind_mouse" : "kind_keyboard");
    public string LinkText => Loc.T(Device.Link switch { LinkKind.Wired => "link_wired", LinkKind.Dongle => "link_dongle", _ => "link_bluetooth" });
    /// <summary>Segoe Fluent glyph: mouse or keyboard.</summary>
    public string Glyph => Device.Kind == DeviceKind.Mouse ? "" : "";

    public DeviceState State
    {
        get => _state;
        protected set
        {
            if (!Set(ref _state, value)) return;
            OnPropertyChanged(nameof(CanWrite));
            OnPropertyChanged(nameof(IsReady));
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool IsReady => State == DeviceState.Ready;
    public bool IsLoading => State is DeviceState.Reading or DeviceState.WaitingWake;
    public bool HasError => State == DeviceState.Error;
    public string StatusText { get => _statusText; protected set => Set(ref _statusText, value); }
    public bool Busy { get => _busy; protected set { if (Set(ref _busy, value)) OnPropertyChanged(nameof(CanWrite)); } }
    public bool IsDirty { get => _dirty; protected set => Set(ref _dirty, value); }
    public string? Toast { get => _toast; private set => Set(ref _toast, value); }

    /// <summary>The backup gate: writing is possible only once the device was read and backed up.</summary>
    public bool CanWrite => State == DeviceState.Ready && !Busy && HasBackup;
    public bool HasBackup => Backups.HasOriginal(Device.DeviceKey);

    public AsyncCommand ApplyCommand { get; }
    public RelayCommand DiscardCommand { get; }
    public AsyncCommand RetryCommand { get; }
    public AsyncCommand BackupNowCommand { get; }
    public AsyncCommand RestoreCommand { get; }
    public RelayCommand ExportBackupCommand { get; }
    public AsyncCommand ImportBackupCommand { get; }
    public RelayCommand OpenBackupFolderCommand { get; }

    protected void MarkDirty() => IsDirty = true;

    public async void ShowToast(string text)
    {
        _toastCts?.Cancel();
        var cts = _toastCts = new CancellationTokenSource();
        Toast = text;
        try { await Task.Delay(4000, cts.Token); Toast = null; }
        catch (TaskCanceledException) { }
    }

    /// <summary>Reads the device, saves a backup (the first becomes ORIGINAL) and loads the editor.</summary>
    public async Task InitializeAsync()
    {
        Busy = true;
        State = DeviceState.Reading;
        StatusText = Loc.T("reading");
        try
        {
            var progress = new Progress<string>(s => StatusText = $"{Loc.T("reading")} ({s})");
            var snapshot = await Device.ReadSnapshotAsync(progress);
            snapshot.AppVersion = App.Version;
            Annotate(snapshot);
            var saved = Backups.Save(snapshot);
            LoadFromSnapshot(snapshot);
            RefreshBackups();
            OnPropertyChanged(nameof(HasBackup));
            State = DeviceState.Ready;
            StatusText = "";
            IsDirty = false;
            if (saved.IsOriginal) ShowToast($"{Loc.T("backup_done")} — {Loc.T("backup_original")}");
        }
        catch (NotSupportedException ex)
        {
            State = DeviceState.Unsupported;
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            State = ex is Core.Devices.X11.MouseAsleepException ? DeviceState.WaitingWake : DeviceState.Error;
            StatusText = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    protected void RefreshBackups()
    {
        BackupList.Clear();
        foreach (var e in Backups.List(Device.DeviceKey)) BackupList.Add(new BackupItemVm(e));
    }

    private async Task BackupNowAsync()
    {
        Busy = true;
        try
        {
            var snap = await Device.ReadSnapshotAsync();
            snap.AppVersion = App.Version;
            Backups.Save(snap);
            RefreshBackups();
            ShowToast(Loc.T("backup_done"));
        }
        finally { Busy = false; }
    }

    private async Task ApplyAsync()
    {
        Busy = true;
        StatusText = Loc.T("applying");
        try
        {
            await WriteChangesAsync();
            IsDirty = false;
            ShowToast(Loc.T("applied"));
        }
        finally
        {
            StatusText = "";
            Busy = false;
        }
    }

    private async Task RestoreAsync(DeviceSnapshot snapshot)
    {
        var when = snapshot.CreatedAt.LocalDateTime.ToString("g");
        if (!App.Confirm(Loc.F("backup_restore_confirm", when))) return;
        Busy = true;
        StatusText = Loc.T("applying");
        try
        {
            // Safety: keep what is on the device right now before overwriting it.
            var current = await Device.ReadSnapshotAsync();
            current.AppVersion = App.Version;
            Backups.Save(current);
            await Device.RestoreSnapshotAsync(snapshot, new Progress<string>(s => StatusText = $"{Loc.T("applying")} ({s})"));
            await RestoreExtrasAsync(snapshot);
        }
        finally
        {
            Busy = false;
            StatusText = "";
        }
        await InitializeAsync();
        ShowToast(Loc.T("backup_restored"));
    }

    private void ExportBackup(BackupItemVm item)
    {
        var dlg = new SaveFileDialog
        {
            Filter = Loc.T("file_filter_backup"),
            FileName = $"{AppPaths.SafeName(Device.Model)}-{item.Entry.Snapshot.CreatedAt:yyyyMMdd-HHmmss}.json",
        };
        if (dlg.ShowDialog() == true) Backups.Export(item.Entry, dlg.FileName);
    }

    private async Task ImportBackupAsync()
    {
        var dlg = new OpenFileDialog { Filter = Loc.T("file_filter_backup") };
        if (dlg.ShowDialog() != true) return;
        var snap = Backups.Import(dlg.FileName);
        if (snap.Model != Device.Model)
            throw new InvalidDataException($"Este backup é de \"{snap.Model}\", não de \"{Device.Model}\".");
        await RestoreAsync(snap);
    }

    private void Discard()
    {
        DiscardChanges();
        IsDirty = false;
    }

    /// <summary>Adds a human-readable summary to a snapshot before it is saved. Default: nothing.</summary>
    protected virtual void Annotate(DeviceSnapshot snapshot) { }
    protected abstract void LoadFromSnapshot(DeviceSnapshot snapshot);
    protected abstract Task WriteChangesAsync();
    protected abstract void DiscardChanges();
    /// <summary>Restores what the snapshot cannot carry (e.g. app-side parts). Default: nothing.</summary>
    protected virtual Task RestoreExtrasAsync(DeviceSnapshot snapshot) => Task.CompletedTask;
    protected virtual void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(LinkText));
    }

    public virtual void Dispose() => _toastCts?.Cancel();
}

/// <summary>Device that is detected but cannot be configured over its current connection.</summary>
public sealed class UnsupportedDeviceVm : DeviceVm
{
    public UnsupportedDeviceVm(ISharkDevice device) : base(device) { }
    protected override void LoadFromSnapshot(DeviceSnapshot snapshot) { }
    protected override Task WriteChangesAsync() => Task.CompletedTask;
    protected override void DiscardChanges() { }
}
