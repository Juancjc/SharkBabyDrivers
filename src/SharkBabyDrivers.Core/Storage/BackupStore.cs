using System.Text.Json;
using SharkBabyDrivers.Core.Devices;

namespace SharkBabyDrivers.Core.Storage;

public sealed record BackupEntry(string Path, DeviceSnapshot Snapshot, bool IsOriginal);

/// <summary>
/// The backup of what each device had stored before SharkBabyDrivers ever wrote to it ("original").
/// It is taken once, on the first connection, and never replaced or deleted. Configurations the user
/// wants to keep go to the <see cref="ProfileLibrary"/> instead.
/// </summary>
public sealed class BackupStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private const string OriginalName = "original.json";

    public string Folder(string deviceKey) => Path.Combine(AppPaths.Backups, AppPaths.SafeName(deviceKey));

    private string OriginalPath(string deviceKey) => Path.Combine(Folder(deviceKey), OriginalName);

    public bool HasOriginal(string deviceKey) => File.Exists(OriginalPath(deviceKey));

    /// <summary>Saves the snapshot as the device's original, unless it already has one. Returns the entry when it was written.</summary>
    public BackupEntry? SaveOriginal(DeviceSnapshot snapshot)
    {
        if (HasOriginal(snapshot.DeviceKey)) return null;
        var path = OriginalPath(snapshot.DeviceKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, Json));
        File.Move(tmp, path, overwrite: false);
        return new BackupEntry(path, snapshot, true);
    }

    public BackupEntry? Original(string deviceKey)
    {
        var path = OriginalPath(deviceKey);
        return File.Exists(path) && TryLoad(path) is { } s ? new BackupEntry(path, s, true) : null;
    }

    public static DeviceSnapshot? TryLoad(string path)
    {
        try { return JsonSerializer.Deserialize<DeviceSnapshot>(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public void Export(BackupEntry entry, string destination) => File.Copy(entry.Path, destination, overwrite: true);

    public DeviceSnapshot Import(string source) =>
        TryLoad(source) ?? throw new InvalidDataException("Arquivo de backup inválido.");
}
