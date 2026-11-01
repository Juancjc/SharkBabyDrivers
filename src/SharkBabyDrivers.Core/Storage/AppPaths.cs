namespace SharkBabyDrivers.Core.Storage;

public static class AppPaths
{
    /// <summary>Root for everything the app stores: %LOCALAPPDATA%\SharkBabyDrivers (overridable for tests).</summary>
    public static string Root { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharkBabyDrivers");

    public static string Backups => Path.Combine(Root, "backups");
    public static string Profiles => Path.Combine(Root, "profiles");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Log => Path.Combine(Root, "sharkbabydrivers.log");

    public static string SafeName(string s) =>
        string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
}
