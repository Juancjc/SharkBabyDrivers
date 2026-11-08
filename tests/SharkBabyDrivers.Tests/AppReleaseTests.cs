using SharkBabyDrivers.Core.Updates;
using System.Text.Json;

namespace SharkBabyDrivers.Tests;

public class AppReleaseTests
{
    private static string Release(string version = "1.9.0", bool preview = false, string url = "https://github.com/Juancjc/SharkBabyDrivers/releases/download/v1.9.0/app.zip", string? digest = null) => JsonSerializer.Serialize(new {
        tag_name = "v" + version, draft = false, prerelease = preview, body = "Novidades",
        assets = new[] { new { name = $"SharkBabyDrivers-{version}-win-x64-portable.zip", browser_download_url = url, digest = digest ?? "sha256:" + new string('a', 64) } }
    });

    [Fact] public void NewStableReleaseIncludesNews() => Assert.Equal("Novidades", AppRelease.Parse(Release(), "1.8.0")!.Notes);
    [Theory]
    [InlineData("1.9.0")]
    [InlineData("2.0.0")]
    public void EqualOrOlderIsIgnored(string current) => Assert.Null(AppRelease.Parse(Release(), current));
    [Fact] public void PreviewIsIgnored() => Assert.Null(AppRelease.Parse(Release(preview: true), "1.8.0"));
    [Fact] public void ForeignDownloadIsRejected() => Assert.Throws<InvalidDataException>(() => AppRelease.Parse(Release(url: "https://example.com/app.zip"), "1.8.0"));
    [Fact] public void MissingHashIsRejected() => Assert.Throws<InvalidDataException>(() => AppRelease.Parse(Release(digest: ""), "1.8.0"));
    [Fact] public void IncompleteReleaseIsIgnored() => Assert.Null(AppRelease.Parse(Release().Replace("win-x64-portable.zip", "win-x64.zip"), "1.8.0"));
}
