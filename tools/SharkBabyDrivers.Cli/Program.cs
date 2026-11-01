using SharkBabyDrivers.Core;
using SharkBabyDrivers.Core.Devices;
using SharkBabyDrivers.Core.Hid;
using SharkBabyDrivers.Core.Storage;

// Diagnostics for SharkBabyDrivers. dump/backup do not change settings; a wired keyboard temporarily selects each profile to read Hall Effect.
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
                    var entry = store.SaveOriginal(snap);
                    Console.WriteLine(entry is null ? "   já existe um backup ORIGINAL; nada foi salvo." : $"   backup ORIGINAL salvo: {entry.Path}");
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

    case "signalrgb-layout":
        // Prints the key table of the SignalRGB plugin (no device access).
        Console.WriteLine(SharkBabyDrivers.Core.Lighting.SignalRgbKeyMap.ToJavaScript());
        return 0;

    default:
        Console.Error.WriteLine("uso: SharkBabyDrivers.Cli [list|dump|backup|probe rid len flag [waitMs]]");
        return 64;
}
