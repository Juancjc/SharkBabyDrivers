using System.Collections.ObjectModel;
using System.Text.Json;
using SharkBabyDrivers.Core.Devices;
using SharkBabyDrivers.Core.Devices.R5;
using SharkBabyDrivers.Core.Storage;
using SharkBabyDrivers.Infrastructure;
using SharkBabyDrivers.Controls;
using SharkBabyDrivers.Core.Devices.X11;

namespace SharkBabyDrivers.ViewModels;

public sealed record R5Choice(byte Value,string Label);
public sealed class R5DpiVm(int index,int x,int y,Action changed) : ObservableObject
{
    private int _x=x,_y=y;
    public string Label => $"DPI {index+1}";
    public int X { get=>_x; set { if(Set(ref _x,value)) changed(); } }
    public int Y { get=>_y; set { if(Set(ref _y,value)) changed(); } }
}
public sealed class R5ButtonVm(int index,byte[] original,Action changed) : ObservableObject
{
    public string Label => new[]{"Esquerdo","Direito","Scroll / meio","Voltar","Avan�ar"}[index];
    public byte[] Payload { get; private set; } = original.ToArray();
    public IReadOnlyList<R5Choice> Choices { get; } = new[]{new R5Choice(255,"Manter atribui��o atual"),new(1,"Clique esquerdo"),new(2,"Clique direito"),new(3,"Clique do meio"),new(4,"Voltar"),new(5,"Avan�ar"),new(16,"Rolar para cima"),new(17,"Rolar para baixo"),new(20,"Alternar DPI"),new(254,"Atalho personalizado"),new(0,"Desativar")};
    private byte _selected=255;
    public byte Selected { get=>_selected; set { if(value==254) { OnPropertyChanged(nameof(Selected)); return; } if(!Set(ref _selected,value)) return; Payload=value==255?original.ToArray():value==20?new byte[]{7,1,6}:value==0?new byte[]{0,0}:new byte[]{1,1,value}; OnPropertyChanged(nameof(Current)); changed(); } }
    public RelayCommand ShortcutCommand => new(() =>
    {
        var initial=Payload.Length==4 && Payload[0]==4 ? ButtonAction.Key((KeyModifiers)Payload[2],Payload[3]) : (ButtonAction?)null;
        var dialog=new ShortcutDialog(initial){Owner=System.Windows.Application.Current.MainWindow};
        if(dialog.ShowDialog()==true && dialog.Result is { } key)
        {
            Payload=[4,2,key.P1,key.P2];_selected=254;OnPropertyChanged(nameof(Selected));OnPropertyChanged(nameof(Current));changed();
        }
    });
    public string Current => $"Tipo {Payload[0]}  {Convert.ToHexString(Payload.AsSpan(2))}";
}
public sealed class R5Vm : DeviceVm
{
    private R5Config _baseline=new(),_draft=new();
    public R5Vm(R5Device device) : base(device) { }
    public ObservableCollection<R5DpiVm> DpiStages { get; }=[];
    public ObservableCollection<R5ButtonVm> Buttons { get; }=[];
    public string ProfileLabel => $"Perfil ativo {_draft.Profile}  R5 Ultra  at� 42.000 DPI";
    public IReadOnlyList<R5Choice> PollingChoices => (Device.Link==LinkKind.Wired
        ? new[]{new R5Choice(8,"125 Hz"),new(4,"250 Hz"),new(2,"500 Hz"),new(1,"1000 Hz")}
        : new[]{new R5Choice(8,"125 Hz"),new(4,"250 Hz"),new(2,"500 Hz"),new(1,"1000 Hz"),new(32,"2000 Hz"),new(64,"4000 Hz"),new(128,"8000 Hz")});
    public IReadOnlyList<R5Choice> LodChoices { get; }=[new(135,"0,7 mm"),new(1,"1 mm"),new(2,"2 mm")];
    public byte Polling { get=>_draft.Polling; set { _draft.Polling=value; MarkDirty(); } }
    public byte Lod { get=>_draft.Lod; set { _draft.Lod=value; MarkDirty(); } }
    public byte ActiveDpi { get=>_draft.ActiveDpi; set { _draft.ActiveDpi=value; MarkDirty(); } }
    public byte Debounce { get=>_draft.Debounce; set { _draft.Debounce=value; MarkDirty(); } }
    public ushort SleepSeconds { get=>_draft.SleepSeconds; set { _draft.SleepSeconds=value; MarkDirty(); } }
    public bool AngleSnap { get=>_draft.AngleSnap; set { _draft.AngleSnap=value; MarkDirty(); } }
    public bool MotionSync { get=>_draft.MotionSync; set { _draft.MotionSync=value; MarkDirty(); } }
    public bool Ripple { get=>_draft.Ripple; set { _draft.Ripple=value; MarkDirty(); } }
    public bool Tracking { get=>_draft.Tracking; set { _draft.Tracking=value; MarkDirty(); } }
    private void Load(R5Config config)
    {
        _draft=config.Clone(); DpiStages.Clear(); Buttons.Clear();
        for(int i=0;i<_draft.Dpi.Length;i++) DpiStages.Add(new R5DpiVm(i,_draft.Dpi[i],_draft.DpiY[i],MarkDirty));
        for(int i=0;i<5;i++) Buttons.Add(new R5ButtonVm(i,_draft.Buttons[i],MarkDirty));
        RefreshAll();
    }
    private R5Config Capture()
    {
        var c=_draft.Clone(); c.Dpi=DpiStages.Select(s=>s.X).ToArray();c.DpiY=DpiStages.Select(s=>s.Y).ToArray();
        c.Buttons=Buttons.Select(b=>b.Payload.ToArray()).ToArray();c.Validate(Device.Link==LinkKind.Wired);return c;
    }
    protected override void LoadFromSnapshot(DeviceSnapshot snapshot) { _baseline=R5Config.FromSnapshot(snapshot); Load(_baseline); }
    protected override async Task WriteChangesAsync()
    {
        var config=Capture(); await ((R5Device)Device).ApplyAsync(config); _baseline=config.Clone(); Load(_baseline);
    }
    protected override void DiscardChanges() => Load(_baseline);
    protected override async Task ResetFromOriginalAsync(DeviceSnapshot original, DeviceSnapshot current, bool profileOnly)
    {
        var config = R5Config.FromSnapshot(original);
        config.Profile = R5Config.FromSnapshot(current).Profile;
        await ((R5Device)Device).ApplyAsync(config);
    }
    protected override object CaptureProfile() => Capture();
    protected override void LoadProfileFile(string path)
    {
        var json=File.ReadAllText(path);
        using var document=JsonDocument.Parse(json);
        if(!document.RootElement.TryGetProperty("Model",out var model) || model.GetString()!=Device.Model)
            throw new InvalidDataException("Este perfil n�o pertence ao R5 Ultra.");
        var config=JsonSerializer.Deserialize<R5Config>(json,JsonStore.Options)
            ?? throw new InvalidDataException("Perfil R5 inv�lido.");
        config.Validate(Device.Link==LinkKind.Wired);
        // A file is an editor preset; applying never switches the onboard profile.
        config.Profile=_baseline.Profile;Load(config);
    }
}
