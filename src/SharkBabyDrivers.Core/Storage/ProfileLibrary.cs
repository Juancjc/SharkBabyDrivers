namespace SharkBabyDrivers.Core.Storage;

public sealed record SavedProfile(string Name, string Path, DateTime SavedAt);

/// <summary>
/// Named configurations the user saved, one folder per device model (so the keyboard shares them between
/// the cable and the 2.4G receiver). Each file has the same format as "Export profile".
/// </summary>
public sealed class ProfileLibrary
{
    public const string Extension = ".sbd.json";

    public static string Folder(string model) => Path.Combine(AppPaths.Root, "saved-profiles", AppPaths.SafeName(model));

    public static string PathFor(string model, string name) => Path.Combine(Folder(model), AppPaths.SafeName(name.Trim()) + Extension);

    public static IReadOnlyList<SavedProfile> List(string model)
    {
        var folder = Folder(model);
        if (!Directory.Exists(folder)) return [];
        return Directory.GetFiles(folder, "*" + Extension)
            .Select(p => new SavedProfile(Path.GetFileName(p)[..^Extension.Length], p, File.GetLastWriteTime(p)))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static bool Exists(string model, string name) => File.Exists(PathFor(model, name));

    public static void Save<T>(string model, string name, T profile) => JsonStore.Save(PathFor(model, name), profile);

    public static void Delete(SavedProfile profile) => File.Delete(profile.Path);
}
