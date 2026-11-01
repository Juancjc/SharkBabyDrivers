using Microsoft.Win32;

namespace SharkBabyDrivers.Services;

/// <summary>"Start with Windows": a per-user Run entry that opens the app in the tray (--tray), no admin needed.</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SharkBabyDrivers";

    private static string Command => $"\"{Environment.ProcessPath}\" --tray";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string v && v.Contains(Path.GetFileName(Environment.ProcessPath ?? "SharkBabyDrivers.exe"), StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Points an existing entry at this copy of the app (it may have moved or been updated).</summary>
    public static void Refresh()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string v && v != Command) key.SetValue(ValueName, Command);
    }
}
