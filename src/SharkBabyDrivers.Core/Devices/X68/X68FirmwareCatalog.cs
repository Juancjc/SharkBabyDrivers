using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharkBabyDrivers.Core.Devices.X68;

public static class X68FirmwareCatalog
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };
    private const string Endpoint = "https://api2.rongyuan.tech:3816/api/v2/get_fw_version";

    public static async Task<string?> GetPublishedVersionAsync(uint deviceId)
    {
        using var response = await Client.PostAsJsonAsync(Endpoint, new { dev_id = deviceId });
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.InternalServerError && body.Trim() == "Record not found")
            return null;

        response.EnsureSuccessStatusCode();
        var result = JsonSerializer.Deserialize<CatalogResponse>(body);
        if (result is { Code: 0, Data.Version: { } version } && !string.IsNullOrWhiteSpace(version))
            return version;

        throw new JsonException("Invalid firmware catalog response.");
    }

    private sealed record CatalogResponse(
        [property: JsonPropertyName("code")] int? Code,
        [property: JsonPropertyName("data")] CatalogEntry? Data);

    private sealed record CatalogEntry([property: JsonPropertyName("version_str")] string? Version);
}
