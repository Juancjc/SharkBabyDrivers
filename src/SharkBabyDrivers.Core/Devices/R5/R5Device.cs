using System.Runtime.Versioning;
using System.Text.Json;
using SharkBabyDrivers.Core.Hid;
using SharkBabyDrivers.Core.Storage;

namespace SharkBabyDrivers.Core.Devices.R5;

public interface IR5Transport : IDisposable
{
    void Send(byte[] frame);
    byte[] Receive();
}

[SupportedOSPlatform("windows")]
public sealed class R5Driver : IDeviceDriver
{
    public string Model => "Attack Shark R5 Ultra";
    public bool Matches(IReadOnlyList<HidDeviceInfo> collections) => collections.Any(IsConfig);
    public static bool IsConfig(HidDeviceInfo c) => c.VendorId == R5Protocol.VendorId
        && c.ProductId is R5Protocol.WiredPid or R5Protocol.DonglePid && c.FeatureReportLength == 65;
    public ISharkDevice Open(IReadOnlyList<HidDeviceInfo> collections)
    {
        var info = collections.First(IsConfig);
        return new R5Device(new R5HidTransport(HidConnection.Open(info)),
            $"r5-{info.VendorId:x4}{info.ProductId:x4}-{info.Serial}", info.ProductId);
    }
    private sealed class R5HidTransport(HidConnection connection) : IR5Transport
    {
        public void Send(byte[] frame) => connection.SetFeature(frame);
        public byte[] Receive() => connection.GetFeature(0,65);
        public void Dispose() => connection.Dispose();
    }
}

public sealed class R5Device(IR5Transport transport, string key, ushort pid) : ISharkDevice
{
    private readonly SemaphoreSlim _queue = new(1,1);
    public int DelayMs { get; init; } = 30;
    public string DeviceKey => key;
    public string Model => "Attack Shark R5 Ultra";
    public DeviceKind Kind => DeviceKind.Mouse;
    public LinkKind Link => pid == R5Protocol.WiredPid ? LinkKind.Wired : LinkKind.Dongle;
    public ushort VendorId => R5Protocol.VendorId;
    public ushort ProductId => pid;
    public async Task<DeviceSnapshot> ReadSnapshotAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        await _queue.WaitAsync(ct);
        try { return await ReadCore(progress,ct); }
        finally { _queue.Release(); }
    }
    private async Task<byte[]> Exchange(byte[] request, CancellationToken ct)
    {
        // A bounded wait ignores unrelated battery/DPI notifications, never accepts stale data as settings.
        ct.ThrowIfCancellationRequested();
        await Task.Run(() => transport.Send(request),ct);
        for (var i=0;i<12;i++)
        {
            await Task.Delay(DelayMs,ct);
            var response = R5Protocol.Normalize(await Task.Run(transport.Receive,ct));
            if (R5Protocol.Matches(request,response)) return response;
        }
        throw new DeviceAsleepException("O R5 não respondeu ao comando. Mova ou clique no mouse e tente novamente.");
    }
    private async Task<DeviceSnapshot> ReadCore(IProgress<string>? progress, CancellationToken ct)
    {
        var snapshot = new DeviceSnapshot { Model=Model, DeviceKey=DeviceKey, VendorId=VendorId, ProductId=ProductId };
        async Task<byte[]> Read(string name, byte len, byte group, byte command, params byte[] data)
        {
            progress?.Report(name);
            var response = await Exchange(R5Protocol.Frame(len,group,command,data),ct);
            snapshot.Put(name,response);
            return response;
        }
        var fw=await Read("firmware",16,0,0x81);
        snapshot.Firmware=string.Join(".",fw.Skip(7).Take(4));
        var profile=(await Read("profile",1,0,0x85))[7];
        if (profile is <1 or >3) throw new InvalidDataException("Perfil R5 inválido.");
        var config=new R5Config { Profile=profile };
        var dpi=await Read("dpi",10,1,0x81,profile,6);
        var count=dpi[8];
        if (count is <1 or >6) throw new InvalidDataException("Estágios DPI R5 inválidos.");
        config.Dpi=Enumerable.Range(0,count).Select(i=>(dpi[9+i*4]<<8)|dpi[10+i*4]).ToArray();
        config.DpiY=Enumerable.Range(0,count).Select(i=>(dpi[11+i*4]<<8)|dpi[12+i*4]).ToArray();
        async Task<byte> Value(string name,byte group,byte command) => (await Read(name,2,group,command,profile))[8];
        config.ActiveDpi=await Value("active_dpi",1,0x82);
        config.Polling=await Value("polling",1,0x80);
        if(config.Polling==16) config.Polling=1; // official firmware alias
        config.Debounce=await Value("debounce",0,0x88);
        config.Lod=await Value("lod",1,0x88);
        config.AngleSnap=await Value("angle_snap",1,0x84)==1;
        config.MotionSync=await Value("motion_sync",1,0x89)==1;
        config.Ripple=await Value("ripple",1,0x8A)==1;
        config.Tracking=await Value("tracking",1,0x93)==1;
        var sleep=await Read("sleep",3,0,0x87,profile);
        config.SleepSeconds=(ushort)((sleep[8]<<8)|sleep[9]);
        for(byte button=1;button<=5;button++)
        {
            var response=await Read($"button_{button}",15,3,0x80,profile,button,0,255,10);
            if(response[7]!=profile || response[8]!=button || response[11]>40)
                throw new InvalidDataException("Resposta de botão R5 inválida.");
            config.Buttons[button-1]=new[]{response[10],response[11]}.Concat(response.Skip(12).Take(response[11])).ToArray();
        }
        config.Validate(Link==LinkKind.Wired);
        snapshot.Notes["r5_config"]=JsonSerializer.Serialize(config,JsonStore.Options);
        snapshot.Notes["summary"]=$"Perfil {profile} • {count} estágios DPI • firmware {snapshot.Firmware}";
        snapshot.Notes["scope"]="Perfil ativo: DPI, sensor, polling, suspensão e botões. Dados de macros e cores DPI não são incluídos; permanecem no dispositivo.";
        return snapshot;
    }
    public async Task ApplyAsync(R5Config desired, CancellationToken ct=default)
    {
        desired=desired.Clone(); desired.Validate(Link==LinkKind.Wired);
        await _queue.WaitAsync(ct);
        try
        {
            var before=R5Config.FromSnapshot(await ReadCore(null,ct));
            if(before.Profile!=desired.Profile) throw new InvalidOperationException("O perfil ativo mudou. Releia o R5 antes de aplicar.");
            for(int i=0;i<5;i++)
                if(!before.Buttons[i].SequenceEqual(desired.Buttons[i]) && desired.Buttons[i][0] is 16 or 17 or 18)
                    throw new NotSupportedException("Transferência de macros R5 ainda não disponível.");
            async Task Set(byte len,byte group,byte command,params byte[] data)
            {
                var response=await Exchange(R5Protocol.Frame(len,group,command,data),ct);
                if(response[7]!=desired.Profile) throw new IOException("Confirmação de perfil R5 inválida.");
            }
            async Task Scalar(byte group,byte command,byte oldValue,byte newValue)
            { if(oldValue!=newValue) await Set(2,group,command,desired.Profile,newValue); }
            if(!before.Dpi.SequenceEqual(desired.Dpi)||!before.DpiY.SequenceEqual(desired.DpiY))
            {
                var payload=new List<byte>{desired.Profile,(byte)desired.Dpi.Length};
                for(int i=0;i<desired.Dpi.Length;i++) payload.AddRange([(byte)(desired.Dpi[i]>>8),(byte)desired.Dpi[i],(byte)(desired.DpiY[i]>>8),(byte)desired.DpiY[i]]);
                await Set(26,1,1,payload.ToArray());
            }
            await Scalar(1,2,before.ActiveDpi,desired.ActiveDpi);
            await Scalar(1,0,before.Polling,desired.Polling);
            await Scalar(0,8,before.Debounce,desired.Debounce);
            await Scalar(1,8,before.Lod,desired.Lod);
            await Scalar(1,4,Convert.ToByte(before.AngleSnap),Convert.ToByte(desired.AngleSnap));
            await Scalar(1,9,Convert.ToByte(before.MotionSync),Convert.ToByte(desired.MotionSync));
            await Scalar(1,10,Convert.ToByte(before.Ripple),Convert.ToByte(desired.Ripple));
            await Scalar(1,19,Convert.ToByte(before.Tracking),Convert.ToByte(desired.Tracking));
            if(before.SleepSeconds!=desired.SleepSeconds) await Set(3,0,7,desired.Profile,(byte)(desired.SleepSeconds>>8),(byte)desired.SleepSeconds);
            // Establish new left-click assignments before replacing any old left-click button.
            foreach(var i in Enumerable.Range(0,5).OrderByDescending(i=>desired.Buttons[i].SequenceEqual(new byte[]{1,1,1})))
                if(!before.Buttons[i].SequenceEqual(desired.Buttons[i]))
                {
                    var b=desired.Buttons[i];
                    if(b[0] is 16 or 17 or 18) throw new NotSupportedException("Macros R5 só podem ser preservadas no próprio dispositivo; criar ou transferir macros ainda não está disponível.");
                    await Set((byte)(5+b[1]),3,0,new byte[]{desired.Profile,(byte)(i+1),0,b[0],b[1]}.Concat(b.Skip(2)).ToArray());
                }
            var after=R5Config.FromSnapshot(await ReadCore(null,ct));
            if(JsonSerializer.Serialize(after)!=JsonSerializer.Serialize(desired))
                throw new IOException("O R5 não confirmou todos os ajustes. O rascunho foi mantido; releia ou tente aplicar novamente.");
        }
        finally { _queue.Release(); }
    }
    public Task RestoreSnapshotAsync(DeviceSnapshot snapshot,IProgress<string>? progress=null,CancellationToken ct=default)
    {
        if(snapshot.Model!=Model || snapshot.VendorId!=VendorId) throw new InvalidDataException("Backup de outro modelo.");
        return ApplyAsync(R5Config.FromSnapshot(snapshot),ct);
    }
    public void Dispose() => transport.Dispose();
}
