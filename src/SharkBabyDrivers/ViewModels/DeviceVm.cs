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

public sealed class SavedProfileVm(SavedProfile profile) : ObservableObject
{
    private bool _isSelected;
    public SavedProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string When => Profile.SavedAt.ToString("g");
    public bool IsSelected { get => _isSelected; internal set => Set(ref _isSelected, value); }
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
    public ObservableCollection<SavedProfileVm> SavedProfiles { get; } = new();
    private string _newProfileName = "";
    public string NewProfileName { get => _newProfileName; set => Set(ref _newProfileName, value); }
    private SavedProfileVm? _selectedSavedProfile;
    public SavedProfileVm? SelectedSavedProfile
    {
        get => _selectedSavedProfile;
        private set
        {
            if (ReferenceEquals(_selectedSavedProfile, value)) return;
            if (_selectedSavedProfile is { } previous) previous.IsSelected = false;
            Set(ref _selectedSavedProfile, value);
            if (value is not null) value.IsSelected = true;
            OnPropertyChanged(nameof(HasSelectedSavedProfile));
            OnPropertyChanged(nameof(ProfileEditorStatus));
        }
    }
    public bool HasSelectedSavedProfile => SelectedSavedProfile is not null;
    public string ProfileEditorStatus => SelectedSavedProfile is { } selected
        ? Loc.F("profile_editing", selected.Name) : Loc.T("profile_draft");

    protected DeviceVm(ISharkDevice device)
    {
        Device = device;
        ApplyCommand = new AsyncCommand(ApplyAsync, () => CanWrite && IsDirty);
        DiscardCommand = new RelayCommand(Discard, () => IsDirty && !Busy);
        RetryCommand = new AsyncCommand(InitializeAsync, () => !Busy);
        ResetDeviceCommand = new AsyncCommand(() => ResetAsync(false), () => CanWrite);
        ResetProfileCommand = new AsyncCommand(() => ResetAsync(true), () => CanWrite);
        RestoreCommand = new AsyncCommand(p => RestoreAsync(((BackupItemVm)p!).Entry.Snapshot), p => p is BackupItemVm && CanWrite);
        ExportBackupCommand = new RelayCommand(p => ExportBackup((BackupItemVm)p!), p => p is BackupItemVm);
        SaveProfileCommand = new RelayCommand(SaveProfile, () => IsReady && !Busy);
        NewProfileCommand = new RelayCommand(NewProfile, () => IsReady && !Busy);
        LoadProfileCommand = new RelayCommand(p => LoadSavedProfile((SavedProfileVm)p!), p => p is SavedProfileVm && IsReady && !Busy);
        DeleteProfileCommand = new RelayCommand(p => DeleteProfile((SavedProfileVm)p!), p => p is SavedProfileVm);
        ExportProfileCommand = new RelayCommand(ExportProfile, () => IsReady);
        ImportProfileCommand = new RelayCommand(ImportProfile, () => IsReady && !Busy);
        OpenBackupFolderCommand = new RelayCommand(() =>
        {
            var folder = Backups.Folder(Device.DeviceKey);
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        });
        Loc.I.PropertyChanged += (_, _) => OnLanguageChanged();
    }

    public string Title => Device.DeviceKey.Contains("demo", StringComparison.Ordinal) ? $"{Device.Model} (demo)" : Device.Model;
    public string NavigationTitle => Title.StartsWith("Attack Shark ", StringComparison.Ordinal)
        ? Title["Attack Shark ".Length..] : Title;
    public string KindText => Loc.T(Device.Kind == DeviceKind.Mouse ? "kind_mouse" : "kind_keyboard");
    public string LinkText => Loc.T(Device.Link switch { LinkKind.Wired => "link_wired", LinkKind.Dongle => "link_dongle", _ => "link_bluetooth" });
    /// <summary>Segoe Fluent glyph: mouse or keyboard.</summary>
    public string Glyph => Device.Kind == DeviceKind.Mouse ? "?" : "?";

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
    public AsyncCommand ResetDeviceCommand { get; }
    public AsyncCommand ResetProfileCommand { get; }

    public async Task ResetAsync(bool profileOnly, bool confirm = true)
    {
        if (!CanWrite) throw new InvalidOperationException(Loc.T("reset_unavailable"));
        if (confirm && !App.Confirm(Loc.T(profileOnly ? "reset_profile_confirm" : "reset_device_confirm"))) return;
        var snapshot = Backups.Original(Device.DeviceKey)?.Snapshot
            ?? throw new InvalidDataException(Loc.T("reset_unavailable"));
        Busy = true;
        StatusText = Loc.T("applying");
        try
        {
            // Keep a recovery point separate from the immutable first-connection backup.
            var current = await Device.ReadSnapshotAsync();
            var folder = Backups.Folder(Device.DeviceKey);
            JsonStore.Save(Path.Combine(folder, $"before-reset-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.json"), current);
            JsonStore.Save(Path.Combine(folder, $"before-reset-profile-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.json"), CaptureProfile());
            await ResetFromOriginalAsync(snapshot, current, profileOnly);
            await InitializeAsync();
            if (!IsReady) throw new IOException(StatusText);
            ShowToast(Loc.T("reset_done"));
        }
        catch
        {
            State = DeviceState.Error;
            StatusText = Loc.T("reset_failed");
            throw;
        }
        finally { Busy = false; }
    }

    protected virtual async Task ResetFromOriginalAsync(DeviceSnapshot original, DeviceSnapshot current, bool profileOnly)
    {
        await Device.RestoreSnapshotAsync(original);
        await RestoreExtrasAsync(original);
    }
    public AsyncCommand RestoreCommand { get; }
    public RelayCommand ExportBackupCommand { get; }
    public RelayCommand SaveProfileCommand { get; }
    public RelayCommand NewProfileCommand { get; }
    public RelayCommand LoadProfileCommand { get; }
    public RelayCommand DeleteProfileCommand { get; }
    public RelayCommand ExportProfileCommand { get; }
    public RelayCommand ImportProfileCommand { get; }
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

    /// <summary>Reads the device, saves the ORIGINAL backup if the device has none yet, and loads the editor.</summary>
    /// <summary>True once the page started reading the device (pages may wait while only the tray icon is up).</summary>
    public bool Started { get; private set; }

    public async Task InitializeAsync()
    {
        Started = true;
        Busy = true;
        State = DeviceState.Reading;
        StatusText = Loc.T("reading");
        try
        {
            var progress = new Progress<string>(s => StatusText = $"{Loc.T("reading")} ({s})");
            var snapshot = await Device.ReadSnapshotAsync(progress);
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(NavigationTitle));
            snapshot.AppVersion = App.Version;
            Annotate(snapshot);
            var saved = Backups.SaveOriginal(snapshot);
            LoadFromSnapshot(snapshot);
            NewProfile();
            RefreshBackups();
            RefreshProfiles();
            OnPropertyChanged(nameof(HasBackup));
            State = DeviceState.Ready;
            StatusText = "";
            IsDirty = false;
            if (saved is not null) ShowToast(Loc.T("backup_done"));
            OnReady();
        }
        catch (NotSupportedException ex)
        {
            State = DeviceState.Unsupported;
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            State = ex is DeviceAsleepException ? DeviceState.WaitingWake : DeviceState.Error;
            StatusText = ex.Message;
            if (State == DeviceState.WaitingWake) _ = RetryWhenAwakeAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Wake hint for the waiting banner.</summary>
    public string WakeHint => Loc.T(Device.Kind == DeviceKind.Mouse ? "wake_hint" : "wake_hint_kb");

    private bool _disposed;

    /// <summary>While the device sleeps behind its receiver, keep trying every few seconds until it answers.</summary>
    private async Task RetryWhenAwakeAsync()
    {
        await Task.Delay(3000);
        if (!_disposed && State == DeviceState.WaitingWake && !Busy) await InitializeAsync();
    }

    protected void RefreshBackups()
    {
        BackupList.Clear();
        if (Backups.Original(Device.DeviceKey) is { } e) BackupList.Add(new BackupItemVm(e));
    }

    // ------------------------------------------------------------------ saved profiles

    protected void RefreshProfiles()
    {
        var selectedPath = SelectedSavedProfile?.Profile.Path;
        SavedProfiles.Clear();
        foreach (var p in ProfileLibrary.List(Device.Model)) SavedProfiles.Add(new SavedProfileVm(p));
        SelectSavedProfile(selectedPath, updateName: false);
    }

    private void SelectSavedProfile(string? path, bool updateName = true)
    {
        SelectedSavedProfile = path is null ? null : SavedProfiles.FirstOrDefault(p =>
            string.Equals(p.Profile.Path, path, StringComparison.OrdinalIgnoreCase));
        if (updateName) NewProfileName = SelectedSavedProfile?.Name ?? "";
    }

    /// <summary>Starts a new named copy of the current editor settings without changing those settings.</summary>
    private void NewProfile()
    {
        SelectedSavedProfile = null;
        NewProfileName = "";
    }

    private void SaveProfile()
    {
        var name = NewProfileName.Trim();
        if (name.Length == 0)
            name = Enumerable.Range(1, 999).Select(i => Loc.F("profile_default_name", i)).First(n => !ProfileLibrary.Exists(Device.Model, n));
        if (ProfileLibrary.Exists(Device.Model, name) && !App.Confirm(Loc.F("profile_overwrite_confirm", name))) return;
        ProfileLibrary.Save(Device.Model, name, CaptureProfile());
        RefreshProfiles();
        SelectSavedProfile(ProfileLibrary.PathFor(Device.Model, name));
        ShowToast(Loc.F("profile_saved", name));
    }

    private void LoadSavedProfile(SavedProfileVm item)
    {
        LoadProfileFile(item.Profile.Path);
        SelectSavedProfile(item.Profile.Path);
        MarkDirty();
        ShowToast(Loc.F("profile_loaded", item.Name));
    }

    private void DeleteProfile(SavedProfileVm item)
    {
        if (!App.Confirm(Loc.F("profile_delete_confirm", item.Name))) return;
        var wasSelected = string.Equals(SelectedSavedProfile?.Profile.Path, item.Profile.Path, StringComparison.OrdinalIgnoreCase);
        ProfileLibrary.Delete(item.Profile);
        RefreshProfiles();
        if (wasSelected) NewProfile();
    }

    private void ExportProfile()
    {
        var dlg = new SaveFileDialog { Filter = Loc.T("file_filter_profile"), FileName = AppPaths.SafeName(Device.Model) + ProfileLibrary.Extension };
        if (dlg.ShowDialog() == true) JsonStore.Save(dlg.FileName, CaptureProfile());
    }

    /// <summary>Loads a profile file into the editor and keeps a copy in the library.</summary>
    private void ImportProfile()
    {
        var dlg = new OpenFileDialog { Filter = Loc.T("file_filter_profile") };
        if (dlg.ShowDialog() != true) return;
        LoadProfileFile(dlg.FileName);
        var name = Path.GetFileName(dlg.FileName);
        name = name.EndsWith(ProfileLibrary.Extension, StringComparison.OrdinalIgnoreCase)
            ? name[..^ProfileLibrary.Extension.Length]
            : Path.GetFileNameWithoutExtension(name);
        // Preserve both the existing library entry and the imported settings when names collide.
        var importedPath = Path.GetFullPath(dlg.FileName);
        var libraryPath = Path.GetFullPath(ProfileLibrary.PathFor(Device.Model, name));
        if (!string.Equals(importedPath, libraryPath, StringComparison.OrdinalIgnoreCase))
        {
            var baseName = name;
            for (var suffix = 2; ProfileLibrary.Exists(Device.Model, name); suffix++) name = $"{baseName} ({suffix})";
            ProfileLibrary.Save(Device.Model, name, CaptureProfile());
        }
        RefreshProfiles();
        SelectSavedProfile(ProfileLibrary.PathFor(Device.Model, name));
        MarkDirty();
        ShowToast(Loc.T("profile_imported"));
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

    private void Discard()
    {
        DiscardChanges();
        NewProfile();
        IsDirty = false;
    }

    /// <summary>Called after a successful read (the ORIGINAL backup exists by then).</summary>
    protected virtual void OnReady() { }

    /// <summary>Adds a human-readable summary to a snapshot before it is saved. Default: nothing.</summary>
    protected virtual void Annotate(DeviceSnapshot snapshot) { }
    protected abstract void LoadFromSnapshot(DeviceSnapshot snapshot);
    protected abstract Task WriteChangesAsync();
    protected abstract void DiscardChanges();
    /// <summary>The editor's current settings in the profile file format of this device.</summary>
    protected abstract object CaptureProfile();
    /// <summary>Loads a profile file into the editor (nothing is written until Apply).</summary>
    protected abstract void LoadProfileFile(string path);
    /// <summary>Restores what the snapshot cannot carry (e.g. app-side parts). Default: nothing.</summary>
    protected virtual Task RestoreExtrasAsync(DeviceSnapshot snapshot) => Task.CompletedTask;
    protected virtual void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(LinkText));
        OnPropertyChanged(nameof(WakeHint));
        OnPropertyChanged(nameof(ProfileEditorStatus));
    }

    public virtual void Dispose()
    {
        _disposed = true;
        _toastCts?.Cancel();
    }
}

/// <summary>Device that is detected but cannot be configured over its current connection.</summary>
public sealed class UnsupportedDeviceVm : DeviceVm
{
    public UnsupportedDeviceVm(ISharkDevice device) : base(device) { }
    protected override void LoadFromSnapshot(DeviceSnapshot snapshot) { }
    protected override Task WriteChangesAsync() => Task.CompletedTask;
    protected override void DiscardChanges() { }
    protected override object CaptureProfile() => new();
    protected override void LoadProfileFile(string path) { }
}
