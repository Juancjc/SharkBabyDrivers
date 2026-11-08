using System.Text.Json;

namespace SharkBabyDrivers.Core.Updates;

public sealed record AppRelease(string Version, string Notes, Uri Download, string Digest)
{
    public static AppRelease? Parse(string json, string current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString()!.TrimStart('v');
        if (!System.Version.TryParse(tag, out var next) || !System.Version.TryParse(current, out var installed) || next <= installed) return null;
        var name = $"SharkBabyDrivers-{tag}-win-x64-portable.zip";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != name) continue;
            var uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
            if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/Juancjc/SharkBabyDrivers/releases/download/", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid update download URL.");
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
            if (!digest.StartsWith("sha256:") || digest.Length != 71) throw new InvalidDataException("Update has no SHA-256 digest.");
            return new(tag, root.GetProperty("body").GetString() ?? "", uri, digest[7..]);
        }
        return null; // A release is usable only after its portable asset is uploaded.
    }
}
