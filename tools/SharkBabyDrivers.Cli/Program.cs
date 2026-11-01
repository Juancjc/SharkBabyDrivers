using SharkBabyDrivers.Core;
using SharkBabyDrivers.Core.Devices;
using SharkBabyDrivers.Core.Hid;
using SharkBabyDrivers.Core.Storage;

// Diagnostics for SharkBabyDrivers. Read-only commands only: nothing here writes settings to a device.
//   list     – HID collections of the supported vendors
//   backup   – reads every supported device and saves a backup (same as the app does on first connect)
//   dump     – prints the decoded configuration without saving

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "list";
switch (command)
{
    case "list":
        foreach (var c in HidEnumerator.Enumerate((v, _) => DeviceCatalog.VendorIds.Contains(v)).OrderBy(c => c.Path))
            Console.WriteLine(c);
        return 0;

    case "backup":
    case "dump":
    {
        using var manager = new DeviceManager(DeviceCatalog.Drivers, DeviceCatalog.VendorIds);
        manager.OpenFailed += (model, ex) => Console.Error.WriteLine($"[{model}] não abriu: {ex.Message}");
        manager.Refresh();
        if (manager.Devices.Count == 0)
        {
            Console.Error.WriteLine("Nenhum dispositivo suportado encontrado.");
            return 2;
        }
        var store = new BackupStore();
        var failures = 0;
        foreach (var device in manager.Devices)
        {
            Console.WriteLine($"== {device.Model} ({device.Link}, {device.VendorId:X4}:{device.ProductId:X4}) key={device.DeviceKey}");
            try
            {
                var snap = await device.ReadSnapshotAsync(new Progress<string>(s => Console.WriteLine($"   lendo {s}…")));
                foreach (var (name, hex) in snap.Reports) Console.WriteLine($"   {name}: {hex}");
                foreach (var (name, text) in snap.Notes) Console.WriteLine($"   {name}: {text}");
                if (command == "backup")
                {
                    var entry = store.Save(snap);
                    Console.WriteLine($"   backup salvo{(entry.IsOriginal ? " (ORIGINAL)" : "")}: {entry.Path}");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"   ERRO: {ex.Message}{(ex.InnerException is null ? "" : " → " + ex.InnerException.Message)}");
            }
        }
        return failures == 0 ? 0 : 1;
    }

    case "x11-rewrite":
    {
        // Idempotent write test: read the mouse config and write the very same bytes back (verified by read-back).
        using var manager = new DeviceManager(DeviceCatalog.Drivers, DeviceCatalog.VendorIds);
        manager.Refresh();
        if (manager.Devices.OfType<SharkBabyDrivers.Core.Devices.X11.X11Device>().FirstOrDefault() is not { } mouse)
        {
            Console.Error.WriteLine("X11 não encontrado.");
            return 2;
        }
        var before = await mouse.RunAsync(mouse.ReadConfig);
        Console.WriteLine("lido:   " + SharkBabyDrivers.Core.Devices.X11.X11Device.Describe(before));
        await mouse.RunAsync(() => mouse.WriteConfig(before.Clone(), null));
        var after = await mouse.RunAsync(mouse.ReadConfig);
        Console.WriteLine("depois: " + SharkBabyDrivers.Core.Devices.X11.X11Device.Describe(after));
        foreach (var (name, a, b) in new[] { ("04", before.Dpi, after.Dpi), ("05", before.Light, after.Light), ("06", before.Poll, after.Poll) })
            Console.WriteLine($"  {name} antes  {Convert.ToHexString(a)}\n  {name} depois {Convert.ToHexString(b)}");
        var same = before.SameAs(after);
        Console.WriteLine(same ? "OK: gravação aceita e configuração idêntica." : "DIFERENTE!");
        return same ? 0 : 1;
    }

    case "probe":
    {
        // probe <rid> <len> <flag> [waitMs] – one raw X11 read handshake, printing every step.
        var rid = Convert.ToByte(args[1], 16);
        var len = int.Parse(args[2]);
        var flag = Convert.ToByte(args[3], 16);
        var wait = args.Length > 4 ? int.Parse(args[4]) : 500;
        var tries = args.Length > 5 ? int.Parse(args[5]) : 3;
        var info = HidEnumerator.Enumerate((v, _) => v == SharkBabyDrivers.Core.Devices.X11.X11Protocol.VendorId)
            .First(c => c.UsagePage == SharkBabyDrivers.Core.Devices.X11.X11Protocol.ConfigUsagePage);
        using var con = HidConnection.Open(info);
        for (var i = 0; i < tries; i++)
        {
            con.SetFeature(SharkBabyDrivers.Core.Devices.X11.X11Protocol.BuildReadRequest(rid, len, flag));
            Thread.Sleep(wait);
            var st = con.GetFeature(0xA0, 8);
            Console.WriteLine($"A0 status: {Convert.ToHexString(st)}");
            var data = con.GetFeature(rid, len);
            Console.WriteLine($"{rid:X2}: {Convert.ToHexString(data)}");
            if (st[1] == 1) break;
        }
        return 0;
    }

    case "kbprobe":
    {
        // kbprobe [cmdHex] – read-only GET through the 2.4G receiver (dongle framing), printing every step.
        var cmd = args.Length > 1 ? Convert.ToByte(args[1], 16) : (byte)0x8F;
        if (cmd < 0x80) { Console.Error.WriteLine("Só comandos GET (>= 0x80)."); return 64; }
        var info = HidEnumerator.Enumerate((v, _) => v == 0x3151).First(c => c.UsagePage == 0xFFFF && c.Usage == 0x02);
        Console.WriteLine($"usando {info}");
        using var con = HidConnection.Open(info);
        static byte[] Frame(params byte[] head)
        {
            var f = new byte[65];
            head.CopyTo(f, 1);
            var sum = 0;
            for (var i = 1; i <= 7; i++) sum += f[i];
            f[8] = (byte)(0xFF - (sum & 0xFF));
            return f;
        }
        static byte[] Forward(byte[] msg)
        {
            // [0x00 report][0x0E][msg0..msg6][checksum over msg0..6] …
            var f = new byte[65];
            f[1] = 0x0E;
            msg.CopyTo(f, 2);
            var sum = 0;
            for (var i = 2; i <= 8; i++) sum += f[i];
            f[9] = (byte)(0xFF - (sum & 0xFF));
            return f;
        }
        void Step(string label, byte[] frame, int sleepMs)
        {
            con.SetFeature(frame);
            Console.WriteLine($"-> {label}: {Convert.ToHexString(frame.AsSpan(1, 10))}");
            Thread.Sleep(sleepMs);
        }
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Step("select keyboard", Frame(0xF6, 0x05), 5);
            Step("write mode", Frame(0xFE, 0x08), 10);
            Step($"forward GET {cmd:X2}", Forward([cmd]), 30);
            Step("read mode", Frame(0xFE, 0x0C), 10);
            Step("fetch", Frame(0xFD), 10);
            var resp = con.GetFeature(0x00, 65);
            Console.WriteLine($"<- {Convert.ToHexString(resp.AsSpan(1, 24))}");
            for (var poll = 0; poll < 15 && resp[1] == 0xAA && resp[2] == 0x55; poll++)
            {
                Thread.Sleep(40);
                if (poll % 3 == 2) Step("fetch again", Frame(0xFD), 10);
                resp = con.GetFeature(0x00, 65);
                Console.WriteLine($"<- {Convert.ToHexString(resp.AsSpan(1, 24))}");
            }
            if (resp[1] == cmd)
            {
                if (cmd == 0x8F)
                    Console.WriteLine($"deviceId={BitConverter.ToUInt32(resp, 2)} fw=0x{resp[9] << 8 | resp[8]:X4}");
                break;
            }
            Thread.Sleep(300);
        }
        return 0;
    }

    default:
        Console.Error.WriteLine("uso: SharkBabyDrivers.Cli [list|dump|backup|probe rid len flag [waitMs]|kbprobe [getCmd]]");
        return 64;
}
