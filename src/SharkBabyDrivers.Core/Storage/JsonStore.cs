using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharkBabyDrivers.Core.Storage;

/// <summary>Small atomic JSON load/save helper for settings and profiles.</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T LoadOrDefault<T>(string path, Func<T> fallback)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? fallback();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // A corrupt file must never stop the app; keep a copy for inspection and start fresh.
            try { File.Copy(path, path + ".corrupt", overwrite: true); } catch { /* best effort */ }
        }
        return fallback();
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

public sealed class AppSettings
{
    /// <summary>"pt-BR" or "en".</summary>
    public string Language { get; set; } = "pt-BR";
    public bool StartMinimized { get; set; }

    /// <summary>Who drives the X68's lighting: <see cref="LightingOwners.App"/> or <see cref="LightingOwners.SignalRgb"/>.</summary>
    public string KeyboardLightingOwner { get; set; } = LightingOwners.App;
    /// <summary>The keyboard's own lighting (hex of the 0x87 answer) from before SignalRGB took over, restored when it hands back.</summary>
    public string? KeyboardLedBeforeSignalRgb { get; set; }
    /// <summary>
    /// How SignalRGB is shown while it owns the lighting: <see cref="SignalRgbStyles.Effects"/> (the keyboard's own
    /// effects imitate SignalRGB's scene) or <see cref="SignalRgbStyles.Live"/> (one live colour only).
    /// </summary>
    public string KeyboardSignalRgbStyle { get; set; } = SignalRgbStyles.Effects;
    /// <summary>True while the keyboard holds a firmware effect written to imitate SignalRGB (not the user's own).</summary>
    public bool KeyboardSignalRgbEffectActive { get; set; }

    public static AppSettings Load() => JsonStore.LoadOrDefault(AppPaths.Settings, () => new AppSettings());
    public void Save() => JsonStore.Save(AppPaths.Settings, this);
}

public static class LightingOwners
{
    public const string App = "app";
    public const string SignalRgb = "signalrgb";
}

public static class SignalRgbStyles
{
    public const string Effects = "effects";
    public const string Live = "live";
}
