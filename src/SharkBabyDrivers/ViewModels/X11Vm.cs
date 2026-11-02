using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Microsoft.Win32;
using SharkBabyDrivers.Core.Devices;
using SharkBabyDrivers.Core.Devices.X11;
using SharkBabyDrivers.Core.Input;
using SharkBabyDrivers.Core.Storage;
using SharkBabyDrivers.Infrastructure;
using SharkBabyDrivers.Localization;

namespace SharkBabyDrivers.ViewModels;

public sealed class Choice<T>(T value, string key, Func<string>? label = null) : ObservableObject
{
    public T Value { get; } = value;
    public string Key { get; } = key;
    public string Label => label?.Invoke() ?? Loc.T(Key);
    public void Refresh() => OnPropertyChanged(nameof(Label));
    public override string ToString() => Label;
}

public sealed class StageVm(X11Vm owner, int index) : ObservableObject
{
    public int Index { get; } = index;
    public int Number => Index + 1;
    public string Name => Loc.F("dpi_stage", Index + 1);
    private X11Config C => owner.Working;

    public bool Enabled
    {
        get => C.IsStageEnabled(Index);
        set
        {
            try { C.SetStageEnabled(Index, value); }
            catch (InvalidOperationException ex) { owner.ShowToast(ex.Message); }
            owner.Changed();
        }
    }

    public int Dpi
    {
        get => C.GetStageDpi(Index);
        set
        {
            C.SetStageDpi(Index, X11Protocol.NormalizeDpi(value));
            owner.Changed();
        }
    }

    public string Color
    {
        get => C.GetStageColor(Index).ToString();
        set
        {
            try { C.SetStageColor(Index, Rgb.Parse(value)); owner.Changed(); }
            catch (FormatException) { }
        }
    }

    public bool IsActive
    {
        get => C.ActiveStage == Index + 1;
        set
        {
            if (!value) return;
            if (!Enabled) Enabled = true;
            C.ActiveStage = Index + 1;
            owner.Changed();
        }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

public sealed class ActionOption : ObservableObject
{
    private string _group = "";
    private string _label = "";
    public required string Id { get; init; }
    public required string Group { get => _group; set => Set(ref _group, value); }
    public required ButtonAction Action { get; init; }
    public required string Label { get => _label; set => Set(ref _label, value); }
    public string? MacroName { get; init; }
    public override string ToString() => Label;
}

public sealed class ButtonVm(X11Vm owner, X11Button button) : ObservableObject
{
    public X11Button Button { get; } = button;
    public ICollectionView ActionView => owner.ActionView;
    public RelayCommand EditShortcutCommand => new(() => owner.EditShortcut(Button));
    public int Number => Button switch
    {
        X11Button.Left => 1, X11Button.Right => 2, X11Button.Middle => 3,
        X11Button.Forward => 4, X11Button.Backward => 5, _ => 6,
    };
    public string AssignmentText
    {
        get
        {
            var option = owner.FindOption(Button);
            return option?.Action.IsKeyboard == true ? X11Vm.DescribeKey(option.Action) : option?.Label ?? "—";
        }
    }
    public string Name => Loc.T(Button switch
    {
        X11Button.Left => "btn_left", X11Button.Right => "btn_right", X11Button.Middle => "btn_middle",
        X11Button.Forward => "btn_forward", X11Button.Backward => "btn_backward", _ => "btn_dpi",
    });

    public ActionOption? Selected
    {
        get => owner.FindOption(Button);
        set
        {
            if (value is null || value.Id == owner.FindOption(Button)?.Id) return;
            owner.Assign(Button, value);
        }
    }

    public string? SelectedActionId => owner.FindOption(Button)?.Id;

    public void Refresh() => OnPropertyChanged(string.Empty);
}

public sealed class MacroStepVm(X11AppProfile.StepDto dto) : ObservableObject
{
    public X11AppProfile.StepDto Dto { get; } = dto;
    public string KeyName => Dto.Key switch
    {
        X11Macro.MouseLeft => Loc.T("act_left_click"), X11Macro.MouseRight => Loc.T("act_right_click"),
        X11Macro.MouseMiddle => Loc.T("act_middle_click"), X11Macro.MouseBack => Loc.T("act_backward"),
        X11Macro.MouseForward => Loc.T("act_forward"), _ => HidUsage.KeyName(Dto.Key),
    };
    public string ActionName => Loc.T(Dto.Pressed ? "macro_press" : "macro_release");
    public int DelayMs { get => Dto.DelayMs; set { Dto.DelayMs = Math.Clamp(value, 1, 60000); OnPropertyChanged(); } }
}

public sealed class MacroVm : ObservableObject
{
    public X11AppProfile.MacroDto Dto { get; }
    public ObservableCollection<MacroStepVm> Steps { get; } = new();
    public MacroVm(X11AppProfile.MacroDto dto)
    {
        Dto = dto;
        foreach (var s in dto.Steps) Steps.Add(new MacroStepVm(s));
    }
    public string Name { get => Dto.Name; set { Dto.Name = value; OnPropertyChanged(); } }
    public MacroPlayMode Mode { get => Dto.Mode; set { Dto.Mode = value; OnPropertyChanged(); } }
    public int Repeat { get => Dto.Repeat; set { Dto.Repeat = Math.Clamp(value, 1, 255); OnPropertyChanged(); } }

    public void Add(byte key, bool pressed, int delay)
    {
        var s = new X11AppProfile.StepDto { Key = key, Pressed = pressed, DelayMs = delay };
        Dto.Steps.Add(s);
        Steps.Add(new MacroStepVm(s));
    }

    public void Remove(MacroStepVm step)
    {
        Dto.Steps.Remove(step.Dto);
        Steps.Remove(step);
    }

    public void Clear()
    {
        Dto.Steps.Clear();
        Steps.Clear();
    }
}

public sealed class X11Vm : DeviceVm
{
    private readonly X11Device _mouse;
    private X11Config _original = X11Config.FactoryDefault();
    private X11AppProfile _savedProfile = new();
    private int? _battery;
    private bool _charging;
    private MacroVm? _selectedMacro;
    private bool _recording;

    public X11Config Working { get; private set; } = X11Config.FactoryDefault();
    public X11AppProfile Profile { get; private set; } = new();

    public ObservableCollection<StageVm> Stages { get; } = new();
    public ObservableCollection<ButtonVm> Buttons { get; } = new();
    public ObservableCollection<MacroVm> Macros { get; } = new();
    public ObservableCollection<ActionOption> ActionOptions { get; } = new();
    public ICollectionView ActionView { get; }
    public IReadOnlyList<Choice<int>> PollingOptions { get; } = X11Protocol.PollingRates.Select(hz => new Choice<int>(hz, "", () => $"{hz} Hz")).ToList();
    public IReadOnlyList<Choice<X11LightMode>> LightModes { get; } = Enum.GetValues<X11LightMode>().Select(m => new Choice<X11LightMode>(m, $"lm_{m}")).ToList();
    public IReadOnlyList<Choice<MacroPlayMode>> MacroModes { get; } = Enum.GetValues<MacroPlayMode>().Select(m => new Choice<MacroPlayMode>(m, $"macro_mode_{m}")).ToList();

    public RelayCommand SetLightModeCommand => new(p => { if (p is X11LightMode m) LightMode = m; });
    public RelayCommand ResetButtonsCommand { get; }
    public RelayCommand NewMacroCommand { get; }
    public RelayCommand DeleteMacroCommand { get; }
    public RelayCommand ClearMacroCommand { get; }
    public RelayCommand RemoveStepCommand { get; }
    public RelayCommand InsertClickCommand { get; }
    public RelayCommand EditShortcutCommand => new(p => { if (p is X11Button button) EditShortcut(button); });
    public AsyncCommand FactoryResetCommand { get; }

    public X11Vm(X11Device device) : base(device)
    {
        _mouse = device;
        _mouse.EventReceived += e => Ui.Post(() => OnMouseEvent(e));
        _mouse.WaitingForWake += () => Ui.Post(() => { State = DeviceState.WaitingWake; StatusText = Loc.T("wake_hint"); });
        for (var i = 0; i < X11Protocol.Stages; i++) Stages.Add(new StageVm(this, i));
        foreach (var b in X11Buttons.Physical) Buttons.Add(new ButtonVm(this, b));
        ActionView = CollectionViewSource.GetDefaultView(ActionOptions);
        ActionView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActionOption.Group)));
        if (ActionView is ICollectionViewLiveShaping { CanChangeLiveGrouping: true } liveView)
            liveView.IsLiveGrouping = true;
        BuildActionOptions();

        ResetButtonsCommand = new RelayCommand(() =>
        {
            Profile.Buttons = X11Buttons.FactoryDefaults().Select(X11AppProfile.ActionDto.From).ToList();
            Profile.ButtonMacros.Clear();
            BuildActionOptions();
            foreach (var b in Buttons) b.Refresh();
            Changed();
        });
        NewMacroCommand = new RelayCommand(() =>
        {
            var name = UniqueMacroName();
            var dto = new X11AppProfile.MacroDto { Name = name };
            Profile.Macros.Add(dto);
            var vm = new MacroVm(dto);
            Macros.Add(vm);
            SelectedMacro = vm;
            BuildActionOptions();
            Changed();
        });
        DeleteMacroCommand = new RelayCommand(() =>
        {
            if (SelectedMacro is not { } m) return;
            Profile.Macros.Remove(m.Dto);
            foreach (var k in Profile.ButtonMacros.Where(kv => kv.Value == m.Name).Select(kv => kv.Key).ToList())
            {
                Profile.ButtonMacros.Remove(k);
                Profile.Buttons[k] = X11AppProfile.ActionDto.From(X11Buttons.FactoryDefaults()[k]);
            }
            Macros.Remove(m);
            SelectedMacro = Macros.FirstOrDefault();
            BuildActionOptions();
            foreach (var b in Buttons) b.Refresh();
            Changed();
        }, () => SelectedMacro is not null);
        ClearMacroCommand = new RelayCommand(() => { SelectedMacro?.Clear(); Changed(); }, () => SelectedMacro is not null);
        RemoveStepCommand = new RelayCommand(p => { if (p is MacroStepVm s) { SelectedMacro?.Remove(s); Changed(); } });
        InsertClickCommand = new RelayCommand(() =>
        {
            SelectedMacro?.Add(X11Macro.MouseLeft, true, 30);
            SelectedMacro?.Add(X11Macro.MouseLeft, false, 30);
            Changed();
        }, () => SelectedMacro is not null);
        FactoryResetCommand = new AsyncCommand(FactoryResetAsync, () => CanWrite);
    }

    // ------------------------------------------------------------------ binding surface

    public int PollingHz
    {
        get => Working.PollingHz;
        set { Working.PollingHz = value; Changed(); }
    }

    public bool IsWired => Device.Link == LinkKind.Wired;
    public bool AngleSnap { get => Working.AngleSnap; set { Working.AngleSnap = value; Changed(); } }
    public bool Ripple { get => Working.Ripple; set { Working.Ripple = value; Changed(); } }
    public int DebounceMs { get => Working.DebounceMs; set { Working.DebounceMs = value; Changed(); } }

    public X11LightMode LightMode
    {
        get => Working.LightMode;
        set { Working.LightMode = value; Changed(); }
    }

    public bool LightUsesColor => LightMode is X11LightMode.Static or X11LightMode.Breathing;
    public bool LightUsesBrightness => LightMode is X11LightMode.Static or X11LightMode.StaticDpi;
    public bool LightUsesSpeed => LightMode is not (X11LightMode.Off or X11LightMode.Static or X11LightMode.StaticDpi);

    public string LightColor
    {
        get => Working.LightColor.ToString();
        set
        {
            try { Working.LightColor = Rgb.Parse(value); Changed(); }
            catch (FormatException) { }
        }
    }

    public int Brightness { get => Working.Brightness; set { Working.Brightness = value; Changed(); } }
    public int Speed { get => Working.Speed; set { Working.Speed = value; Changed(); } }
    public double SleepMinutes { get => Working.SleepMinutes; set { Working.SleepMinutes = value; Changed(); } }
    public int DeepSleepMinutes { get => Working.DeepSleepMinutes; set { Working.DeepSleepMinutes = value; Changed(); } }

    public int? Battery { get => _battery; private set { Set(ref _battery, value); OnPropertyChanged(nameof(BatteryText)); } }
    public bool Charging { get => _charging; private set { Set(ref _charging, value); OnPropertyChanged(nameof(BatteryText)); } }
    public string BatteryText => Battery is { } b ? $"{b}%{(Charging ? " · " + Loc.T("charging") : "")}" : "—";
    public string FirmwareText { get; private set; } = "";

    public MacroVm? SelectedMacro { get => _selectedMacro; set => Set(ref _selectedMacro, value); }
    public bool Recording { get => _recording; set => Set(ref _recording, value); }

    /// <summary>Called by every setter: refresh dependent bindings and mark the page dirty.</summary>
    public void Changed()
    {
        foreach (var s in Stages) s.Refresh();
        OnPropertyChanged(string.Empty);
        MarkDirty();
    }

    // ------------------------------------------------------------------ buttons

    private void BuildActionOptions()
    {
        var options = new List<ActionOption>();
        foreach (var p in X11Actions.Presets)
            options.Add(new ActionOption { Id = p.Id, Group = Loc.T($"grp_{p.Group}"), Action = p.Action, Label = Loc.T($"act_{p.Id}") });
        foreach (var m in Profile.Macros)
            options.Add(new ActionOption { Id = "macro:" + m.Name, Group = Loc.T("tab_macros"), Action = ButtonAction.Macro(0), Label = Loc.F("act_macro", m.Name), MacroName = m.Name });
        // Custom shortcuts currently in use get their own entry so the combo can show them.
        foreach (var dto in Profile.Buttons.Where(d => d.Code == ButtonAction.KeyboardCode))
        {
            var a = dto.ToAction();
            if (X11Actions.FindPreset(a) is null && options.All(o => o.Action != a))
                options.Add(new ActionOption { Id = "key:" + a, Group = Loc.T("grp_Shortcut"), Action = a, Label = Loc.F("act_key", DescribeKey(a)) });
        }
        options.Add(new ActionOption { Id = "custom", Group = Loc.T("grp_Shortcut"), Action = X11Actions.Disabled, Label = Loc.T("custom_shortcut") });

        // Keep unchanged options attached to their selectors while a device/profile finishes loading.
        // Clearing the shared view invalidates selection in controls that are still being bound.
        for (var i = 0; i < options.Count; i++)
        {
            var next = options[i];
            var existing = ActionOptions.FirstOrDefault(o => o.Id == next.Id && o.Action == next.Action && o.MacroName == next.MacroName);
            if (existing is null) ActionOptions.Insert(i, next);
            else
            {
                existing.Label = next.Label;
                existing.Group = next.Group;
                if (ActionOptions.IndexOf(existing) != i) ActionOptions.Move(ActionOptions.IndexOf(existing), i);
            }
        }
        while (ActionOptions.Count > options.Count) ActionOptions.RemoveAt(ActionOptions.Count - 1);
    }

    public static string DescribeKey(ButtonAction a)
    {
        var m = (KeyModifiers)a.P1;
        var parts = new List<string>();
        if (m.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (m.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (m.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (m.HasFlag(KeyModifiers.Win)) parts.Add("Win");
        if (a.P2 != 0) parts.Add(HidUsage.KeyName(a.P2));
        return string.Join(" + ", parts);
    }

    public ActionOption? FindOption(X11Button button)
    {
        var slot = (int)button;
        if (Profile.ButtonMacros.TryGetValue(slot, out var macro))
            return ActionOptions.FirstOrDefault(o => o.MacroName == macro);
        var a = Profile.Buttons[slot].ToAction();
        return ActionOptions.FirstOrDefault(o => o.Id != "custom" && o.MacroName is null && o.Action == a)
               ?? new ActionOption { Id = "raw", Group = "", Action = a, Label = Loc.F("act_unknown", $"{a.Code:X2}{a.P1:X2}{a.P2:X2}") };
    }

    /// <summary>Set from the UI. "custom" asks the view to capture a shortcut first.</summary>
    public Func<ButtonAction?, ButtonAction?>? CaptureShortcut { get; set; }

    public void EditShortcut(X11Button button)
    {
        var current = Profile.Buttons[(int)button].ToAction();
        if (CaptureShortcut?.Invoke(current.IsKeyboard ? current : null) is not { } captured) return;
        Assign(button, new ActionOption { Id = "key:" + captured, Group = "", Action = captured, Label = "" });
    }

    public void Assign(X11Button button, ActionOption option)
    {
        var slot = (int)button;
        if (option.Id == "custom")
        {
            EditShortcut(button);
            Buttons.First(b => b.Button == button).Refresh();
            return;
        }
        if (option.MacroName is { } name)
        {
            Profile.ButtonMacros[slot] = name;
            Profile.Buttons[slot] = X11AppProfile.ActionDto.From(ButtonAction.Macro(X11Buttons.MacroSlotId(button)));
        }
        else
        {
            Profile.ButtonMacros.Remove(slot);
            Profile.Buttons[slot] = X11AppProfile.ActionDto.From(option.Action);
        }
        // The mouse must keep at least one left click, or it becomes unusable.
        if (X11Buttons.Physical.All(b => Profile.ButtonMacros.ContainsKey((int)b) || Profile.Buttons[(int)b].ToAction() != X11Actions.Left))
        {
            ShowToast(Loc.T("btn_left") + ": " + Loc.T("act_left_click") + " ✓");
            Profile.Buttons[(int)X11Button.Left] = X11AppProfile.ActionDto.From(X11Actions.Left);
            Profile.ButtonMacros.Remove((int)X11Button.Left);
        }
        BuildActionOptions();
        foreach (var b in Buttons) b.Refresh();
        Changed();
    }

    // ------------------------------------------------------------------ macros

    private string UniqueMacroName()
    {
        for (var i = 1; ; i++)
        {
            var n = $"Macro {i}";
            if (Profile.Macros.All(m => m.Name != n)) return n;
        }
    }

    public void RenameMacro(MacroVm macro, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || Profile.Macros.Any(m => m != macro.Dto && m.Name == newName)) return;
        foreach (var k in Profile.ButtonMacros.Where(kv => kv.Value == macro.Name).Select(kv => kv.Key).ToList())
            Profile.ButtonMacros[k] = newName;
        macro.Name = newName;
        BuildActionOptions();
        foreach (var b in Buttons) b.Refresh();
        Changed();
    }

    // ------------------------------------------------------------------ device events

    private void OnMouseEvent(X11Event e)
    {
        switch (e)
        {
            case X11Event.Battery b:
                Battery = b.Percent;
                Charging = b.State == BatteryState.Charging;
                break;
            case X11Event.DpiStage d when d.Stage is >= 1 and <= X11Protocol.Stages:
                // The DPI button changed the active stage on the mouse itself: follow it without marking dirty.
                if (Working.IsStageEnabled(d.Stage - 1)) Working.ActiveStage = d.Stage;
                if (_original.IsStageEnabled(d.Stage - 1)) _original.ActiveStage = d.Stage;
                foreach (var s in Stages) s.Refresh();
                break;
        }
    }

    // ------------------------------------------------------------------ DeviceVm

    protected override void LoadFromSnapshot(DeviceSnapshot snapshot)
    {
        _original = new X11Config(snapshot.Get("report_04")!, snapshot.Get("report_05")!, snapshot.Get("report_06")!);
        Working = _original.Clone();
        FirmwareText = snapshot.Firmware;
        Profile = X11AppProfile.Load(Device.DeviceKey);
        _savedProfile = Profile.Clone();
        ReloadMacros();
        BuildActionOptions();
        foreach (var b in Buttons) b.Refresh();
        foreach (var s in Stages) s.Refresh();
        OnPropertyChanged(string.Empty);
    }

    private void ReloadMacros()
    {
        Macros.Clear();
        foreach (var m in Profile.Macros) Macros.Add(new MacroVm(m));
        SelectedMacro = Macros.FirstOrDefault();
    }

    protected override async Task WriteChangesAsync()
    {
        var desired = Working.Clone();
        var before = _original;
        var buttonsChanged = !Profile.ButtonsWritten
            ? Profile.Buttons.Zip(X11Buttons.FactoryDefaults()).Any(p => p.First.ToAction() != p.Second) || Profile.ButtonMacros.Count > 0
            : JsonEqual(Profile.Buttons, _savedProfile.Buttons) is false || JsonEqual(Profile.ButtonMacros, _savedProfile.ButtonMacros) is false
              || JsonEqual(Profile.Macros, _savedProfile.Macros) is false;
        var slots = Profile.Buttons.Select(b => b.ToAction()).ToList();
        var macros = Profile.ButtonMacros
            .Select(kv => (Slot: kv.Key, Macro: Profile.Macros.FirstOrDefault(m => m.Name == kv.Value)))
            .Where(x => x.Macro is not null)
            .Select(x => (SlotId: X11Buttons.MacroSlotId((X11Button)x.Slot), Def: x.Macro!.ToDefinition()))
            .ToList();
        foreach (var (_, def) in macros) X11Macro.BuildLogical(1, def); // validates length before touching the device

        await _mouse.RunAsync(() =>
        {
            _mouse.WriteConfig(desired, before);
            if (buttonsChanged)
            {
                foreach (var (slotId, def) in macros) _mouse.WriteMacro(slotId, def);
                _mouse.WriteButtons(slots);
            }
        });
        _original = desired.Clone();
        if (buttonsChanged) Profile.ButtonsWritten = true;
        Profile.Save(Device.DeviceKey);
        _savedProfile = Profile.Clone();
    }

    private static bool JsonEqual<T>(T a, T b) =>
        System.Text.Json.JsonSerializer.Serialize(a, JsonStore.Options) == System.Text.Json.JsonSerializer.Serialize(b, JsonStore.Options);

    protected override void DiscardChanges()
    {
        Working = _original.Clone();
        Profile = _savedProfile.Clone();
        ReloadMacros();
        BuildActionOptions();
        foreach (var b in Buttons) b.Refresh();
        foreach (var s in Stages) s.Refresh();
        OnPropertyChanged(string.Empty);
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        BuildActionOptions();
        foreach (var c in LightModes) c.Refresh();
        foreach (var c in MacroModes) c.Refresh();
        foreach (var b in Buttons) b.Refresh();
        foreach (var s in Stages) s.Refresh();
        foreach (var m in Macros) foreach (var st in m.Steps) st.RefreshAll();
        OnPropertyChanged(string.Empty);
    }

    // ------------------------------------------------------------------ profile files

    protected override object CaptureProfile()
    {
        var p = Profile.Clone();
        p.Reports = new Dictionary<string, string>
        {
            ["report_04"] = Convert.ToHexString(Working.Dpi),
            ["report_05"] = Convert.ToHexString(Working.Light),
            ["report_06"] = Convert.ToHexString(Working.Poll),
        };
        p.ButtonsWritten = false;
        return p;
    }

    protected override void LoadProfileFile(string path)
    {
        var p = JsonStore.LoadOrDefault<X11AppProfile?>(path, () => null) ?? throw new InvalidDataException("Perfil inválido.");
        if (p.Reports is { } r)
            Working = new X11Config(Convert.FromHexString(r["report_04"]), Convert.FromHexString(r["report_05"]), Convert.FromHexString(r["report_06"]));
        p.Reports = null;
        p.ButtonsWritten = Profile.ButtonsWritten;
        Profile = p;
        ReloadMacros();
        BuildActionOptions();
        foreach (var b in Buttons) b.Refresh();
        foreach (var s in Stages) s.Refresh();
        Changed();
    }

    private async Task FactoryResetAsync()
    {
        if (!App.Confirm(Loc.T("factory_reset_confirm"))) return;
        await _mouse.RunAsync(_mouse.FactoryReset);
        Profile = new X11AppProfile { ButtonsWritten = true };
        Profile.Save(Device.DeviceKey);
        await InitializeAsync();
    }
}
