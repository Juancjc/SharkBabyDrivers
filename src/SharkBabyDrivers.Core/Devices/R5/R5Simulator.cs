using SharkBabyDrivers.Core.Devices;
namespace SharkBabyDrivers.Core.Devices.R5;

/// <summary>Protocol emulator for isolated preview and tests; never accesses USB.</summary>
public sealed class R5Simulator : IR5Transport
{
    public R5Config Config { get; set; } = new();
    public List<byte[]> Sent { get; }=[];
    public bool Awake { get; set; }=true;
    public bool IgnoreWrites { get; set; }
    public bool ShortHeader { get; set; }
    private byte[] _request=new byte[65];
    public void Send(byte[] frame)
    {
        _request=frame.ToArray(); Sent.Add(frame.ToArray());
        if(!Awake || IgnoreWrites || frame[6]>=128) return;
        byte group=frame[5],cmd=frame[6],value=frame[8];
        if(group==1)
        {
            switch(cmd)
            {
                case 0:Config.Polling=value;break;
                case 1:
                    Config.Dpi=Enumerable.Range(0,frame[8]).Select(i=>(frame[9+i*4]<<8)|frame[10+i*4]).ToArray();
                    Config.DpiY=Enumerable.Range(0,frame[8]).Select(i=>(frame[11+i*4]<<8)|frame[12+i*4]).ToArray();break;
                case 2:Config.ActiveDpi=value;break;
                case 4:Config.AngleSnap=value==1;break;
                case 8:Config.Lod=value;break;
                case 9:Config.MotionSync=value==1;break;
                case 10:Config.Ripple=value==1;break;
                case 19:Config.Tracking=value==1;break;
            }
        }
        if(group==0 && cmd==8) Config.Debounce=value;
        if(group==0 && cmd==7) Config.SleepSeconds=(ushort)((value<<8)|frame[9]);
        if(group==3 && cmd==0) Config.Buttons[value-1]=new[]{frame[10],frame[11]}.Concat(frame.Skip(12).Take(frame[11])).ToArray();
    }
    public byte[] Receive()
    {
        var p=_request;var r=new byte[64];
        if(Awake)
        {
            r[1]=0xA1;r[3]=p[3];r[4]=p[4];r[5]=p[5];r[6]=p[6];r[7]=p[7];
            if(p[5]==0)
            {
                switch(p[6])
                {
                    case 0x81:r[7]=0;r[8]=0;r[9]=12;r[10]=0;break;
                    case 0x85:r[7]=Config.Profile;break;
                    case 0x88:r[8]=Config.Debounce;break;
                    case 0x87:r[8]=(byte)(Config.SleepSeconds>>8);r[9]=(byte)Config.SleepSeconds;break;
                }
            }
            if(p[5]==1)
            {
                switch(p[6])
                {
                    case 0x81:r[8]=(byte)Config.Dpi.Length;for(int i=0;i<Config.Dpi.Length;i++){r[9+i*4]=(byte)(Config.Dpi[i]>>8);r[10+i*4]=(byte)Config.Dpi[i];r[11+i*4]=(byte)(Config.DpiY[i]>>8);r[12+i*4]=(byte)Config.DpiY[i];}break;
                    case 0x80:r[8]=Config.Polling;break;
                    case 0x82:r[8]=Config.ActiveDpi;break;
                    case 0x84:r[8]=Convert.ToByte(Config.AngleSnap);break;
                    case 0x88:r[8]=Config.Lod;break;
                    case 0x89:r[8]=Convert.ToByte(Config.MotionSync);break;
                    case 0x8A:r[8]=Convert.ToByte(Config.Ripple);break;
                    case 0x93:r[8]=Convert.ToByte(Config.Tracking);break;
                }
            }
            if(p[5]==3 && p[6]==0x80)
            {
                r[8]=p[8];var b=Config.Buttons[p[8]-1];r[10]=b[0];r[11]=b[1];b.AsSpan(2).CopyTo(r.AsSpan(12));
            }
        }
        var result=new byte[65];
        if(ShortHeader) r.AsSpan(1).CopyTo(result.AsSpan(1));else r.CopyTo(result,1);
        return result;
    }
    public void Dispose() { }
}
