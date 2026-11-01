using System.Text.Json;
using SharkBabyDrivers.Core.Devices;

namespace SharkBabyDrivers.Core.Storage;

public sealed record BackupEntry(string Path, DeviceSnapshot Snapshot, bool IsOriginal);

/// <summary>
/// Backups of what is stored inside each device. The very first backup of a device is kept
/// forever as "original" (the configuration it had before SharkBabyDrivers ever wrote to it).
/// </summary>
public sealed class BackupStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private const string OriginalName = "original.json";

    public string Folder(string deviceKey) => Path.Combine(AppPaths.Backups, AppPaths.SafeName(deviceKey));

    public bool HasOriginal(string deviceKey) => File.Exists(Path.Combine(Folder(deviceKey), OriginalName));

    /// <summary>Saves a snapshot. The first one for a device becomes the permanent original.</summary>
    public BackupEntry Save(DeviceSnapshot snapshot)
    {
        var folder = Folder(snapshot.DeviceKey);
        Directory.CreateDirectory(folder);
        var isOriginal = !HasOriginal(snapshot.DeviceKey);
        var name = isOriginal ? OriginalName : $"{snapshot.CreatedAt:yyyyMMdd-HHmmss}.json";
        var path = Path.Combine(folder, name);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, Json));
        File.Move(tmp, path, overwrite: true);
        return new BackupEntry(path, snapshot, isOriginal);
    }

    public IReadOnlyList<BackupEntry> List(string deviceKey)
    {
        var folder = Folder(deviceKey);
        if (!Directory.Exists(folder)) return [];
        return Directory.GetFiles(folder, "*.json")
            .Select(p => TryLoad(p) is { } s ? new BackupEntry(p, s, Path.GetFileName(p) == OriginalName) : null)
            .Where(e => e is not null)
            .Select(e => e!)
            .OrderByDescending(e => e.IsOriginal)
            .ThenByDescending(e => e.Snapshot.CreatedAt)
            .ToList();
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
