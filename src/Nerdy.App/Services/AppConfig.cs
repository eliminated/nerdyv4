using System.Text.Json;

namespace Nerdy.App.Services;

/// <summary>
/// Build-time configuration from appsettings.json next to the executable. The API address can be overridden
/// with the NERDY_API_URL environment variable (handy for pointing a dev build at a deployed API).
/// </summary>
public sealed class AppConfig
{
    public required Uri ApiBaseUrl { get; init; }

    public static AppConfig Load()
    {
        var url = "http://localhost:5080";
        var file = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(file))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty("Api", out var api) &&
                api.TryGetProperty("BaseUrl", out var baseUrl) && baseUrl.GetString() is { Length: > 0 } configured)
                url = configured;
        }
        if (Environment.GetEnvironmentVariable("NERDY_API_URL") is { Length: > 0 } overridden) url = overridden;
        return new AppConfig { ApiBaseUrl = new Uri(url.TrimEnd('/') + "/") };
    }
}
