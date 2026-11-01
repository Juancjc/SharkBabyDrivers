using System.Reflection;

namespace SharkBabyDrivers.Services;

public enum PluginState { Missing, Outdated, Installed }

/// <summary>
/// The SignalRGB plugin shipped with the app (integrations/signalrgb). SignalRGB loads user plugins from
/// Documents\WhirlwindFX\Plugins, and one there replaces its stock plugin for the same USB ids
/// (its log says "HID plugin with id 0x3151:0x5026 already exists. Overwriting with new path").
/// </summary>
public static class SignalRgbPlugin
{
    public const string FileName = "Attack_Shark_X68_Pro_HE_SharkBabyDrivers.js";

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WhirlwindFX", "Plugins");

    public static string TargetPath => Path.Combine(Folder, FileName);

    public static string Content()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SignalRgbPlugin.js")
                           ?? throw new InvalidOperationException("Plugin do SignalRGB não está embutido no aplicativo.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static PluginState State()
    {
        try
        {
            if (!File.Exists(TargetPath)) return PluginState.Missing;
            return File.ReadAllText(TargetPath) == Content() ? PluginState.Installed : PluginState.Outdated;
        }
        catch (IOException) { return PluginState.Missing; }
        catch (UnauthorizedAccessException) { return PluginState.Missing; }
    }

    public static void Install()
    {
        Directory.CreateDirectory(Folder);
        var tmp = TargetPath + ".tmp";
        File.WriteAllText(tmp, Content());
        File.Move(tmp, TargetPath, overwrite: true);
    }
}
