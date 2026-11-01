using System.Collections.ObjectModel;
using Microsoft.Win32;
using SharkBabyDrivers.Core.Devices;
using SharkBabyDrivers.Core.Devices.X68;
using SharkBabyDrivers.Core.Input;
using SharkBabyDrivers.Core.Storage;
using SharkBabyDrivers.Infrastructure;
using SharkBabyDrivers.Localization;
using static SharkBabyDrivers.Core.Devices.X68.X68Protocol;

namespace SharkBabyDrivers.ViewModels;

public enum KeyLayer { Base, FnWin, FnMac }

public enum RemapKind { Key, Mouse, Media, System, Macro, Profile, Fn, Disabled, Default }

public sealed class KeyCapVm(X68Vm owner, KeyCap cap) : ObservableObject
{
    public const double Unit = 46;
    private bool _selected;

    public KeyCap Cap { get; } = cap;
    public double Left => Cap.X * Unit;
    public double Top => Cap.Y * Unit;
    public double Width => Cap.Width * Unit - 4;
    public double Height => Unit - 4;
    public string Label => Cap.Label;
    public string Mapping => owner.MappingText(Cap.Slot);
    public bool IsChanged => owner.IsRemapped(Cap.Slot);
    public string HeBadge => owner.HeBadge(Cap.Slot);
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
}

public sealed class DksBindingVm(X68Vm owner, int index) : ObservableObject
{
    public int Index { get; } = index;
    public string Title => $"{Index + 1}";
    public string ActionText => owner.DksAction(Index).Describe();

    private byte Stage { get => owner.DksStageByte(Index); set => owner.SetDksStageByte(Index, value); }
    private int Phase(int p) => (Stage >> (p * 2)) & 3;
    private void SetPhase(int p, int v) => Stage = (byte)((Stage & ~(3 << (p * 2))) | ((v & 3) << (p * 2)));

    public int P0 { get => Phase(0); set { SetPhase(0, value); RefreshAll(); } }
    public int P1 { get => Phase(1); set { SetPhase(1, value); RefreshAll(); } }
    public int P2 { get => Phase(2); set { SetPhase(2, value); RefreshAll(); } }
    public int P3 { get => Phase(3); set { SetPhase(3, value); RefreshAll(); } }
}

public sealed class X68MacroVm : ObservableObject
{
    public int Index { get; }
    public ObservableCollection<X68StepVm> Steps { get; } = new();
    private int _repeat;
    public X68MacroVm(int index, IEnumerable<X68Macro.Step> steps, int repeat)
    {
        Index = index;
        _repeat = repeat;
        foreach (var s in steps) Steps.Add(new X68StepVm(s));
    }
    public string Name => $"Macro {Index + 1}";
    public int Repeat { get => _repeat; set => Set(ref _repeat, Math.Clamp(value, 1, 65535)); }
    public byte[] Encode() => X68Macro.Encode(Steps.Select(s => s.ToStep()).ToList(), Repeat);
}

public sealed class X68StepVm(X68Macro.Step step) : ObservableObject
{
    private int _delay = step.DelayMs;
    public byte Code { get; } = step.Code;
    public bool Down { get; } = step.Down;
    public string KeyName => Code switch
    {
        X68Macro.MouseLeft => Loc.T("act_left_click"), X68Macro.MouseRight => Loc.T("act_right_click"),
        X68Macro.MouseMiddle => Loc.T("act_middle_click"), X68Macro.MouseBack => Loc.T("act_backward"),
        X68Macro.MouseForward => Loc.T("act_forward"), _ => HidUsage.KeyName(Code),
    };
    public string ActionName => Loc.T(Down ? "macro_press" : "macro_release");
    public int DelayMs { get => _delay; set => Set(ref _delay, Math.Clamp(value, 1, 65535)); }
    public X68Macro.Step ToStep() => new(Code, Down, DelayMs);
}

public sealed class X68Vm : DeviceVm
{
    private readonly X68Device _kb;
    private X68Image _original = new();
    private int _editProfile;
    private KeyLayer _layer = KeyLayer.Base;
    private KeyCapVm? _focusKey;
    private X68MacroVm? _selectedMacro;
    private bool _recording;
    private bool _calibrating;

    public X68Image Img { get; private set; } = new();
    public X68Model Model => Img.Model;
    public ObservableCollection<KeyCapVm> Caps { get; } = new();
    public ObservableCollection<DksBindingVm> DksBindings { get; } = new();
    public ObservableCollection<X68MacroVm> Macros { get; } = new();
    public double BoardWidth => Model.Width * KeyCapVm.Unit;
    public double BoardHeight => Model.Height * KeyCapVm.Unit;

    public IReadOnlyList<Choice<int>> ProfileChoices { get; } = Enumerable.Range(0, Profiles).Select(p => new Choice<int>(p, "", () => $"{p + 1}")).ToList();
    public IReadOnlyList<Choice<KeyLayer>> LayerChoices { get; } = Enum.GetValues<KeyLayer>().Select(l => new Choice<KeyLayer>(l, $"layer_{l}")).ToList();
    public IReadOnlyList<Choice<RemapKind>> RemapKinds { get; } = Enum.GetValues<RemapKind>().Select(k => new Choice<RemapKind>(k, $"remap_{k}")).ToList();
    public IReadOnlyList<Choice<byte>> KeyChoices { get; } = HidUsage.KeyNames.OrderBy(k => k.Key).Select(k => new Choice<byte>(k.Key, "", () => k.Value)).ToList();
    public IReadOnlyList<Choice<byte>> MouseChoices { get; } =
    [
        new(0xF0, "act_left_click"), new(0xF1, "act_right_click"), new(0xF2, "act_middle_click"),
        new(0xF3, "act_backward"), new(0xF4, "act_forward"),
    ];
    public IReadOnlyList<Choice<ushort>> MediaChoices { get; } = HidUsage.ConsumerNames.Select(kv => new Choice<ushort>(kv.Key, $"cons_{kv.Key:X4}", () => Loc.I[$"cons_{kv.Key:X4}"] is var t && t != $"cons_{kv.Key:X4}" ? t : kv.Value)).ToList();
    public IReadOnlyList<Choice<byte>> SystemChoices { get; } = [new(0x81, "sys_power"), new(0x82, "sys_sleep"), new(0x83, "sys_wake")];
    public IReadOnlyList<Choice<X68MacroMode>> MacroModes { get; } = Enum.GetValues<X68MacroMode>().Select(m => new Choice<X68MacroMode>(m, $"kmacro_mode_{m}")).ToList();
    public IReadOnlyList<Choice<KeyMode>> KeyModes { get; } = Enum.GetValues<KeyMode>().Select(m => new Choice<KeyMode>(m, $"kmode_{m}")).ToList();
    public IReadOnlyList<Choice<int>> PollingChoices { get; } = PollingRates.Select(hz => new Choice<int>(hz, "", () => $"{hz} Hz")).ToList();
    public IReadOnlyList<Choice<X68LightMode>> LightModes { get; } = X68Lighting.Modes.Select(m => new Choice<X68LightMode>(m.Mode, $"klm_{m.Mode}")).ToList();
    public IReadOnlyList<Choice<int>> DksPhaseChoices { get; } = Enumerable.Range(0, 4).Select(v => new Choice<int>(v, $"dks_phase_{v}")).ToList();
    public IReadOnlyList<Choice<int>> SwitchChoices { get; } = SwitchTypes.All.Select(s => new Choice<int>(s.Id, "", () => s.Name)).ToList();

    public RelayCommand SetEditProfileCommand => new(p => { if (p is int v) EditProfile = v; });
    public RelayCommand SetLightModeCommand => new(p => { if (p is X68LightMode m) LightMode = m; });
    public RelayCommand SetPollingCommand => new(p => { if (p is int hz) PollingHz = hz; });
    public RelayCommand SelectKeyCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand AssignCommand { get; }
    public RelayCommand NewMacroCommand { get; }
    public RelayCommand DeleteMacroCommand { get; }
    public RelayCommand ClearMacroCommand { get; }
    public RelayCommand RemoveStepCommand { get; }
    public AsyncCommand ActivateProfileCommand { get; }
    public AsyncCommand CalibrateCommand { get; }
    public AsyncCommand FactoryResetCommand { get; }
    public RelayCommand ExportProfileCommand { get; }
    public RelayCommand ImportProfileCommand { get; }

    public X68Vm(X68Device device) : base(device)
    {
        _kb = device;
        _kb.VendorEvent += e => Ui.Post(() => OnVendorEvent(e));
        SelectKeyCommand = new RelayCommand(p => SelectKey((KeyCapVm)p!));
        SelectAllCommand = new RelayCommand(() => { foreach (var c in Caps) c.IsSelected = true; OnSelectionChanged(); });
        SelectNoneCommand = new RelayCommand(() => { foreach (var c in Caps) c.IsSelected = false; OnSelectionChanged(); });
        AssignCommand = new RelayCommand(AssignToFocus, () => FocusKey is not null);
        NewMacroCommand = new RelayCommand(() =>
        {
            var idx = Enumerable.Range(0, 32).First(i => Macros.All(m => m.Index != i));
            var m = new X68MacroVm(idx, [], 1);
            Macros.Add(m);
            SelectedMacro = m;
            Img.Macros[idx] = m.Encode();
            MarkDirty();
        });
        DeleteMacroCommand = new RelayCommand(() =>
        {
            if (SelectedMacro is not { } m) return;
            Macros.Remove(m);
            Img.Macros.Remove(m.Index);
            SelectedMacro = Macros.FirstOrDefault();
            MarkDirty();
        }, () => SelectedMacro is not null);
        ClearMacroCommand = new RelayCommand(() => { SelectedMacro?.Steps.Clear(); SyncMacro(); }, () => SelectedMacro is not null);
        RemoveStepCommand = new RelayCommand(p => { if (p is X68StepVm s) { SelectedMacro?.Steps.Remove(s); SyncMacro(); } });
        ActivateProfileCommand = new AsyncCommand(async () =>
        {
            await _kb.RunAsync(() => _kb.SelectProfile(EditProfile));
            Img.ActiveProfile = _original.ActiveProfile = EditProfile;
            OnPropertyChanged(nameof(IsEditingActive));
            OnPropertyChanged(nameof(ActiveProfileText));
        }, () => CanWrite && !IsEditingActive);
        CalibrateCommand = new AsyncCommand(ToggleCalibrationAsync, () => CanWrite || Calibrating);
        FactoryResetCommand = new AsyncCommand(FactoryResetAsync, () => CanWrite);
        ExportProfileCommand = new RelayCommand(ExportProfile, () => IsReady);
        ImportProfileCommand = new RelayCommand(ImportProfile, () => IsReady);
    }

    // ------------------------------------------------------------------ profile / layer / selection

    public int EditProfile
    {
        get => _editProfile;
        set
        {
            if (!Set(ref _editProfile, value)) return;
            RefreshBoard();
            OnSelectionChanged();
            OnPropertyChanged(nameof(IsEditingActive));
        }
    }

    public bool IsEditingActive => EditProfile == Img.ActiveProfile;
    public string ActiveProfileText => Loc.F("kb_active_profile", Img.ActiveProfile + 1);

    public KeyLayer Layer
    {
        get => _layer;
        set { if (Set(ref _layer, value)) { RefreshBoard(); LoadRemapEditor(); } }
    }

    private byte[] CurrentMatrix => Layer switch
    {
        KeyLayer.FnWin => Img.FnWin,
        KeyLayer.FnMac => Img.FnMac,
        _ => Img.Keys[EditProfile][0],
    };

    private byte[] DefaultMatrix => Layer switch
    {
        KeyLayer.FnWin => Model.DefaultFnWin,
        KeyLayer.FnMac => Model.DefaultFnMac,
        _ => Model.DefaultKeys,
    };

    public string MappingText(int slot)
    {
        var a = X68Action.Read(CurrentMatrix, slot);
        if (Layer == KeyLayer.Base && a == Model.DefaultAction(slot)) return "";
        return a.IsDisabled ? Loc.T("remap_Disabled") : a.Describe();
    }

    public bool IsRemapped(int slot) => X68Action.Read(CurrentMatrix, slot) != X68Action.Read(DefaultMatrix, slot);

    public string HeBadge(int slot)
    {
        var mode = Img.GetKeyMode(EditProfile, slot);
        var rt = Img.GetRapidTrigger(EditProfile, slot) ? "RT" : "";
        var m = mode switch { KeyMode.Dks => "DKS", KeyMode.ModTap => "MT", KeyMode.ToggleHold or KeyMode.ToggleRepeat => "TGL", KeyMode.SnapTap => "SOCD", _ => "" };
        return string.Join(" ", new[] { m, rt }.Where(s => s.Length > 0));
    }

    public KeyCapVm? FocusKey
    {
        get => _focusKey;
        private set { if (Set(ref _focusKey, value)) { LoadRemapEditor(); OnPropertyChanged(nameof(FocusKeyName)); } }
    }

    public string FocusKeyName => FocusKey?.Label ?? Loc.T("kb_pick_key");
    public int SelectedCount => Caps.Count(c => c.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    private IEnumerable<int> SelectedSlots => Caps.Where(c => c.IsSelected).Select(c => c.Cap.Slot);
    private int Primary => FocusKey?.Cap.Slot ?? SelectedSlots.FirstOrDefault();

    /// <summary>Plain click: focus one key. Ctrl-click (multi = true from the view): toggle in the selection.</summary>
    public bool MultiSelect { get; set; }

    private void SelectKey(KeyCapVm cap)
    {
        if (!MultiSelect)
            foreach (var c in Caps) c.IsSelected = c == cap;
        else
            cap.IsSelected = !cap.IsSelected;
        FocusKey = cap;
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        LoadHeEditor();
    }

    private void RefreshBoard()
    {
        foreach (var c in Caps) c.RefreshAll();
    }

    // ------------------------------------------------------------------ remap editor

    private RemapKind _remapKind;
    private byte _remapKey = 0x04;
    private KeyMods _mods;
    private byte _remapMouse = 0xF0;
    private ushort _remapMedia = 0x00CD;
    private byte _remapSystem = 0x82;
    private int _remapMacro;
    private X68MacroMode _remapMacroMode;
    private int _remapProfile;

    public RemapKind RemapKind { get => _remapKind; set { Set(ref _remapKind, value); OnPropertyChanged(string.Empty); } }
    public byte RemapKey { get => _remapKey; set => Set(ref _remapKey, value); }
    public bool ModCtrl { get => _mods.HasFlag(KeyMods.LCtrl); set => SetMod(KeyMods.LCtrl, value); }
    public bool ModShift { get => _mods.HasFlag(KeyMods.LShift); set => SetMod(KeyMods.LShift, value); }
    public bool ModAlt { get => _mods.HasFlag(KeyMods.LAlt); set => SetMod(KeyMods.LAlt, value); }
    public bool ModWin { get => _mods.HasFlag(KeyMods.LWin); set => SetMod(KeyMods.LWin, value); }
    private void SetMod(KeyMods m, bool on) { _mods = on ? _mods | m : _mods & ~m; OnPropertyChanged(string.Empty); }
    public byte RemapMouse { get => _remapMouse; set => Set(ref _remapMouse, value); }
    public ushort RemapMedia { get => _remapMedia; set => Set(ref _remapMedia, value); }
    public byte RemapSystem { get => _remapSystem; set => Set(ref _remapSystem, value); }
    public int RemapMacro { get => _remapMacro; set => Set(ref _remapMacro, value); }
    public X68MacroMode RemapMacroMode { get => _remapMacroMode; set => Set(ref _remapMacroMode, value); }
    public int RemapProfile { get => _remapProfile; set => Set(ref _remapProfile, value); }
    public string FocusCurrentText => FocusKey is null ? "" : X68Action.Read(CurrentMatrix, FocusKey.Cap.Slot) is var a && a.IsDisabled ? Loc.T("remap_Disabled") : X68Action.Read(CurrentMatrix, FocusKey!.Cap.Slot).Describe();

    private void LoadRemapEditor()
    {
        if (FocusKey is null) return;
        var a = X68Action.Read(CurrentMatrix, FocusKey.Cap.Slot);
        switch (a.Kind)
        {
            case X68ActionKind.Key when a.IsDisabled: _remapKind = RemapKind.Disabled; break;
            case X68ActionKind.Key: _remapKind = RemapKind.Key; _remapKey = a.B2; _mods = (KeyMods)a.B1; break;
            case X68ActionKind.Mouse: _remapKind = RemapKind.Mouse; _remapMouse = a.B2; break;
            case X68ActionKind.Consumer: _remapKind = RemapKind.Media; _remapMedia = (ushort)(a.B2 | a.B3 << 8); break;
            case X68ActionKind.System: _remapKind = RemapKind.System; _remapSystem = a.B2; break;
            case X68ActionKind.Macro: _remapKind = RemapKind.Macro; _remapMacro = a.B2; _remapMacroMode = (X68MacroMode)a.B1; break;
            case X68ActionKind.Profile: _remapKind = RemapKind.Profile; _remapProfile = a.B3; break;
            case X68ActionKind.Function when a.B1 == 1: _remapKind = RemapKind.Fn; break;
            default: _remapKind = RemapKind.Default; break;
        }
        OnPropertyChanged(string.Empty);
    }

    public X68Action BuildRemapAction(int slot) => RemapKind switch
    {
        RemapKind.Key => X68Action.Key(RemapKey, _mods),
        RemapKind.Mouse => X68Action.MouseButton(RemapMouse),
        RemapKind.Media => X68Action.Consumer(RemapMedia),
        RemapKind.System => X68Action.System(RemapSystem),
        RemapKind.Macro => X68Action.Macro(RemapMacro, RemapMacroMode),
        RemapKind.Profile => X68Action.ProfileSelect(RemapProfile),
        RemapKind.Fn => X68Action.Fn,
        RemapKind.Disabled => X68Action.Disabled,
        _ => X68Action.Read(DefaultMatrix, slot),
    };

    private void AssignToFocus()
    {
        var targets = Layer == KeyLayer.Base && SelectedCount > 1 ? SelectedSlots.ToList() : [FocusKey!.Cap.Slot];
        foreach (var slot in targets)
        {
            var action = BuildRemapAction(slot);
            if (Layer == KeyLayer.Base && slot == Model.FnSlot() && action != X68Action.Fn && Model.DefaultAction(slot) == X68Action.Fn)
            {
                ShowToast(Loc.T("kb_keep_fn"));
                continue;
            }
            action.Write(CurrentMatrix, slot);
            if (action.Kind == X68ActionKind.Macro && !Img.Macros.ContainsKey(action.B2))
            {
                var m = new X68MacroVm(action.B2, [], 1);
                Macros.Add(m);
                Img.Macros[action.B2] = m.Encode();
            }
        }
        RefreshBoard();
        OnPropertyChanged(nameof(FocusCurrentText));
        MarkDirty();
    }

    // ------------------------------------------------------------------ Hall effect editor (applies to the selection)

    private double Mm(HeOp op) => Img.Mm(Img.HeValue(EditProfile, op, Primary));

    private void SetAll(HeOp op, double mm)
    {
        var raw = Img.Raw(mm);
        foreach (var s in SelectedSlots) Img.SetHeValue(EditProfile, op, s, raw);
        MarkDirty();
        OnPropertyChanged(string.Empty);
        RefreshBoard();
    }

    public double TravelMax => 4.0;
    public double TravelStep => 1.0 / Img.Divisor;
    public bool HasTopDeadZone => Img.HasTopDeadZone;

    public KeyMode HeMode
    {
        get => Img.GetKeyMode(EditProfile, Primary);
        set
        {
            foreach (var s in SelectedSlots)
            {
                Img.SetKeyMode(EditProfile, s, value, Img.GetRapidTrigger(EditProfile, s));
                if (value == KeyMode.ModTap && Img.He(EditProfile, HeOp.MtTime)[s] == 0) Img.He(EditProfile, HeOp.MtTime)[s] = 20;
                if (value == KeyMode.Dks && Img.HeValue(EditProfile, HeOp.DksTravel, s) == 0) Img.SetHeValue(EditProfile, HeOp.DksTravel, s, Img.Raw(1.5));
            }
            MarkDirty();
            LoadHeEditor();
            RefreshBoard();
        }
    }

    public bool IsDks => HasSelection && HeMode == KeyMode.Dks;
    public bool IsModTap => HasSelection && HeMode == KeyMode.ModTap;
    public bool IsToggle => HasSelection && HeMode is KeyMode.ToggleHold or KeyMode.ToggleRepeat;
    public bool IsSnap => HasSelection && HeMode == KeyMode.SnapTap;
    public bool IsTravelMode => HasSelection && HeMode is KeyMode.Normal or KeyMode.SnapTap;

    public bool RapidTrigger
    {
        get => Img.GetRapidTrigger(EditProfile, Primary);
        set
        {
            foreach (var s in SelectedSlots)
            {
                Img.SetKeyMode(EditProfile, s, Img.GetKeyMode(EditProfile, s), value);
                if (value && Img.HeValue(EditProfile, HeOp.RtPress, s) == 0)
                {
                    Img.SetHeValue(EditProfile, HeOp.RtPress, s, Img.Raw(0.3));
                    Img.SetHeValue(EditProfile, HeOp.RtRelease, s, Img.Raw(0.3));
                }
            }
            MarkDirty();
            OnPropertyChanged(string.Empty);
            RefreshBoard();
        }
    }

    public double Actuation { get => Mm(HeOp.Actuation); set => SetAll(HeOp.Actuation, value); }
    public double ReleasePoint { get => Mm(HeOp.Release); set => SetAll(HeOp.Release, value); }
    public double RtPress { get => Mm(HeOp.RtPress); set => SetAll(HeOp.RtPress, value); }
    public double RtRelease { get => Mm(HeOp.RtRelease); set => SetAll(HeOp.RtRelease, value); }
    public double BottomDeadZone { get => Mm(HeOp.BottomDeadZone); set => SetAll(HeOp.BottomDeadZone, value); }
    public double DksTravel { get => Mm(HeOp.DksTravel); set => SetAll(HeOp.DksTravel, value); }

    public double TopDeadZone
    {
        get => Img.Mm(Img.HeValue(EditProfile, HeOp.TopDeadZone, Primary));
        set
        {
            var raw = Math.Clamp(Img.Raw(value), 0, 255);
            foreach (var s in SelectedSlots) Img.SetHeValue(EditProfile, HeOp.TopDeadZone, s, raw);
            MarkDirty();
            OnPropertyChanged(string.Empty);
        }
    }

    public int MtTimeMs
    {
        get => Img.He(EditProfile, HeOp.MtTime)[Primary] * 10;
        set
        {
            foreach (var s in SelectedSlots) Img.He(EditProfile, HeOp.MtTime)[s] = (byte)Math.Clamp(value / 10, 1, 255);
            MarkDirty();
            OnPropertyChanged(string.Empty);
        }
    }

    public int SwitchType
    {
        get => Img.He(EditProfile, HeOp.SwitchType)[Primary];
        set
        {
            foreach (var s in SelectedSlots) Img.He(EditProfile, HeOp.SwitchType)[s] = (byte)value;
            MarkDirty();
            OnPropertyChanged(string.Empty);
        }
    }

    /// <summary>MT/TGL/DKS bindings live in the four "son" matrices at the key's slot.</summary>
    private X68Action SonAction(int son) => X68Action.Read(Img.Keys[EditProfile][son], Primary);

    private void SetSonAction(int son, X68Action a)
    {
        foreach (var s in SelectedSlots) a.Write(Img.Keys[EditProfile][son], s);
        MarkDirty();
        OnPropertyChanged(string.Empty);
        foreach (var b in DksBindings) b.RefreshAll();
    }

    public string MtHoldText => SonAction(0).Describe();
    public string MtTapText => SonAction(1).Describe();
    public RelayCommand SetMtHoldCommand => new(() => SetSonAction(0, BuildRemapAction(Primary)));
    public RelayCommand SetMtTapCommand => new(() => SetSonAction(1, BuildRemapAction(Primary)));
    public RelayCommand SetToggleCommand => new(() => SetSonAction(0, BuildRemapAction(Primary)));
    public RelayCommand SetDksBindingCommand => new(p => SetSonAction(Convert.ToInt32(p), BuildRemapAction(Primary)));

    public X68Action DksAction(int i) => SonAction(i);
    public byte DksStageByte(int i) => Img.DksStage(EditProfile, Primary, i);
    public void SetDksStageByte(int i, byte v)
    {
        foreach (var s in SelectedSlots) Img.SetDksStage(EditProfile, s, i, v);
        MarkDirty();
    }

    public int? SnapPartner
    {
        get => IsSnap ? Img.He(EditProfile, HeOp.SnapPartner)[Primary] : null;
        set
        {
            if (value is not { } partner || FocusKey is null) return;
            var me = FocusKey.Cap.Slot;
            Img.SetKeyMode(EditProfile, me, KeyMode.SnapTap, Img.GetRapidTrigger(EditProfile, me));
            Img.SetKeyMode(EditProfile, partner, KeyMode.SnapTap, Img.GetRapidTrigger(EditProfile, partner));
            Img.He(EditProfile, HeOp.SnapPartner)[me] = (byte)partner;
            Img.He(EditProfile, HeOp.SnapPartner)[partner] = (byte)me;
            MarkDirty();
            OnPropertyChanged(string.Empty);
            RefreshBoard();
        }
    }

    public IReadOnlyList<Choice<int>> SnapChoices => Caps.Where(c => c != FocusKey).Select(c => new Choice<int>(c.Cap.Slot, "", () => c.Label)).ToList();

    private void LoadHeEditor()
    {
        DksBindings.Clear();
        for (var i = 0; i < 4; i++) DksBindings.Add(new DksBindingVm(this, i));
        OnPropertyChanged(string.Empty);
    }

    // ------------------------------------------------------------------ lighting & options

    public X68LightMode LightMode
    {
        get => Img.LightMode;
        set
        {
            Img.LightMode = value;
            if (LightOption >= LightOptionCount) Img.LightOption = 0;
            MarkDirty();
            OnPropertyChanged(string.Empty);
        }
    }

    private LightModeInfo LightInfo => X68Lighting.Modes.FirstOrDefault(m => m.Mode == Img.LightMode) ?? X68Lighting.Modes[0];
    public int LightOptionCount => LightInfo.Options;
    public bool HasLightOptions => LightInfo.Options > 0;
    public bool HasLightSpeed => LightInfo.HasSpeed;
    public bool HasLightColor => LightInfo.HasColor;
    public IReadOnlyList<Choice<int>> LightOptionChoices => Enumerable.Range(0, LightInfo.Options)
        .Select(i => new Choice<int>(i, $"klo_{LightInfo.Mode}_{i}", () => Loc.I[$"klo_{LightInfo.Mode}_{i}"] is var t && !t.StartsWith("klo_") ? t : $"{i + 1}")).ToList();
    public int LightOption { get => Img.LightOption; set { Img.LightOption = value; MarkDirty(); OnPropertyChanged(); } }
    public int LightBrightness { get => Img.LightBrightness; set { Img.LightBrightness = value; MarkDirty(); OnPropertyChanged(); } }
    public int LightSpeed { get => Img.LightSpeed; set { Img.LightSpeed = value; MarkDirty(); OnPropertyChanged(); } }
    public bool LightRainbow { get => Img.LightRainbow; set { Img.LightRainbow = value; MarkDirty(); OnPropertyChanged(string.Empty); } }
    public string LightColor
    {
        get => $"#{Img.LightColor:X6}";
        set
        {
            try { Img.LightColor = Convert.ToUInt32(value.TrimStart('#'), 16); Img.LightRainbow = false; MarkDirty(); OnPropertyChanged(string.Empty); }
            catch (FormatException) { }
        }
    }

    public int PollingHz { get => Img.PollingHz; set { Img.PollingHz = value; MarkDirty(); OnPropertyChanged(); } }
    public int Debounce { get => Img.DebounceValue; set { Img.DebounceValue = value; MarkDirty(); OnPropertyChanged(); } }
    public bool AutoOs { get => Img.AutoOsDetect; set { Img.AutoOsDetect = value; MarkDirty(); OnPropertyChanged(); } }
    public bool AntiMistouch { get => Img.AntiMistouch; set { Img.AntiMistouch = value; MarkDirty(); OnPropertyChanged(); } }
    public int RtStability { get => Img.RtStability; set { Img.RtStability = value; MarkDirty(); OnPropertyChanged(); } }
    public bool WasdSwap { get => Img.WasdSwap; set { Img.WasdSwap = value; MarkDirty(); OnPropertyChanged(); } }
    public double SleepMinutes { get => Img.SleepSeconds24 / 60.0; set { Img.SleepSeconds24 = (int)Math.Round(value * 60); Img.SleepSecondsBt = Img.SleepSeconds24; MarkDirty(); OnPropertyChanged(); } }
    public double DeepSleepMinutes { get => Img.DeepSleepSeconds24 / 60.0; set { Img.DeepSleepSeconds24 = (int)Math.Round(value * 60); Img.DeepSleepSecondsBt = Img.DeepSleepSeconds24; MarkDirty(); OnPropertyChanged(); } }
    public string FirmwareText => $"USB {Img.UsbFirmware:X4}{(Img.RfFirmware != 0 ? $" · RF {Img.RfFirmware:X4}" : "")} · id {Img.DeviceId}";
    public string OsText => Loc.T(Img.OsMode switch { 1 => "os_mac", 2 => "os_ios", 3 => "os_android", _ => "os_win" });

    // ------------------------------------------------------------------ macros

    public X68MacroVm? SelectedMacro { get => _selectedMacro; set => Set(ref _selectedMacro, value); }
    public bool Recording { get => _recording; set => Set(ref _recording, value); }

    public void AddMacroStep(byte code, bool down, int delay)
    {
        if (SelectedMacro is null) return;
        SelectedMacro.Steps.Add(new X68StepVm(new X68Macro.Step(code, down, delay)));
        SyncMacro();
    }

    public void SyncMacro()
    {
        if (SelectedMacro is not { } m) return;
        try { Img.Macros[m.Index] = m.Encode(); MarkDirty(); }
        catch (ArgumentException ex) { m.Steps.RemoveAt(m.Steps.Count - 1); ShowToast(ex.Message); }
    }

    // ------------------------------------------------------------------ calibration

    public bool Calibrating { get => _calibrating; private set => Set(ref _calibrating, value); }

    private async Task ToggleCalibrationAsync()
    {
        if (!Calibrating)
        {
            if (!App.Confirm(Loc.T("calibrate_confirm"))) return;
            await _kb.RunAsync(_kb.StartCalibration);
            Calibrating = true;
        }
        else
        {
            await _kb.RunAsync(_kb.StopCalibration);
            Calibrating = false;
            ShowToast(Loc.T("calibrate_done"));
        }
    }

    // ------------------------------------------------------------------ events from the keyboard

    private void OnVendorEvent(byte[] e)
    {
        if (e.Length < 2 || Busy || !IsReady) return;
        // Profile switched with Fn on the keyboard: follow it (no write involved).
        if (e[1] == 0x01 && e.Length > 2 && e[2] < Profiles)
        {
            Img.ActiveProfile = _original.ActiveProfile = e[2];
            OnPropertyChanged(nameof(ActiveProfileText));
            OnPropertyChanged(nameof(IsEditingActive));
        }
    }

    // ------------------------------------------------------------------ DeviceVm

    protected override void Annotate(DeviceSnapshot snapshot)
    {
        var img = X68Image.FromSnapshot(snapshot);
        snapshot.Notes["summary"] = $"{img.Model.Name} · {Loc.F("kb_active_profile", img.ActiveProfile + 1)} · {img.PollingHz} Hz · {img.Macros.Count} macro(s)";
    }

    protected override void LoadFromSnapshot(DeviceSnapshot snapshot)
    {
        _original = X68Image.FromSnapshot(snapshot);
        Img = _original.Clone();
        RebuildAll();
    }

    private void RebuildAll()
    {
        Caps.Clear();
        foreach (var cap in Model.Caps) Caps.Add(new KeyCapVm(this, cap));
        Macros.Clear();
        foreach (var (idx, raw) in Img.Macros)
        {
            var (steps, repeat) = X68Macro.Decode(raw);
            Macros.Add(new X68MacroVm(idx, steps, repeat));
        }
        SelectedMacro = Macros.FirstOrDefault();
        _editProfile = Img.ActiveProfile;
        FocusKey = Caps.FirstOrDefault(c => c.Label == "A");
        if (FocusKey is not null) FocusKey.IsSelected = true;
        LoadHeEditor();
        OnPropertyChanged(string.Empty);
    }

    protected override async Task WriteChangesAsync()
    {
        foreach (var m in Macros) Img.Macros[m.Index] = m.Encode();
        var desired = Img.Clone();
        var before = _original;
        await _kb.RunAsync(() => _kb.WriteImage(desired, before));
        _original = desired.Clone();
    }

    protected override void DiscardChanges()
    {
        var profile = EditProfile;
        Img = _original.Clone();
        RebuildAll();
        EditProfile = profile;
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var l in new IEnumerable<ObservableObject>[] { LayerChoices, RemapKinds, MouseChoices, MediaChoices, SystemChoices, MacroModes, KeyModes, LightModes, DksPhaseChoices })
            foreach (var c in l) c.RefreshAll();
        foreach (var m in Macros) foreach (var s in m.Steps) s.RefreshAll();
        RefreshBoard();
        OnPropertyChanged(string.Empty);
    }

    private void ExportProfile()
    {
        var dlg = new SaveFileDialog { Filter = Loc.T("file_filter_profile"), FileName = "x68.sbd.json" };
        if (dlg.ShowDialog() != true) return;
        foreach (var m in Macros) Img.Macros[m.Index] = m.Encode();
        JsonStore.Save(dlg.FileName, Img.ToSnapshot(Device.DeviceKey, Device.Model));
    }

    private void ImportProfile()
    {
        var dlg = new OpenFileDialog { Filter = Loc.T("file_filter_profile") };
        if (dlg.ShowDialog() != true) return;
        var snap = JsonStore.LoadOrDefault<DeviceSnapshot?>(dlg.FileName, () => null) ?? throw new InvalidDataException("Perfil inválido.");
        var img = X68Image.FromSnapshot(snap);
        // Keep identity/firmware of the connected keyboard; take every setting from the file.
        img.DeviceId = Img.DeviceId; img.UsbFirmware = Img.UsbFirmware; img.RfFirmware = Img.RfFirmware;
        Img = img;
        RebuildAll();
        MarkDirty();
        ShowToast(Loc.T("profile_imported"));
    }

    private async Task FactoryResetAsync()
    {
        if (!App.Confirm(Loc.T("factory_reset_confirm"))) return;
        var snap = await _kb.ReadSnapshotAsync();
        snap.AppVersion = App.Version;
        Backups.Save(snap);
        await _kb.RunAsync(_kb.FactoryReset);
        await InitializeAsync();
    }
}

/// <summary>Switch types of the RY5088 firmware (only the ones with an English/Portuguese name the user might recognize are labelled).</summary>
public static class SwitchTypes
{
    public sealed record Info(int Id, string Name);
    public static readonly IReadOnlyList<Info> All =
    [
        new(0, "Gateron (padrão)"), new(1, "Magnetic Jade"), new(2, "Magnetic Jade Pro"), new(3, "Magnetic Jade Gaming"),
        new(4, "Tianwang"), new(5, "Magneto"), new(6, "Kailh/Ketai"), new(8, "Magnetic White"), new(10, "Magneto Pro"),
        new(11, "Tianwang SE"), new(12, "Magneto RGB"), new(13, "Magneto POM"), new(15, "Kailh"), new(16, "TTC Magneto"),
        new(17, "Crystal White"), new(19, "Outemu"), new(29, "Cherry MX Magnetic"), new(33, "Ice Jade"),
        new(39, "Gateron Blue Ice"), new(41, "Gateron Storm"), new(64, "Magnetic Jade Max"), new(79, "Ice Jade Pro"),
    ];
}
