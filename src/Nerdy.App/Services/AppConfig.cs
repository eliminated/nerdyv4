using System.Text.Json;

namespace Nerdy.App.Services;

/// <summary>
/// Build-time configuration from appsettings.json next to the executable (the deployed API), overridden by
/// appsettings.Development.json, which only Debug builds include (the API on this PC). The API address can also be
/// overridden with the NERDY_API_URL environment variable (handy for pointing a dev build at a deployed API).
/// </summary>
public sealed class AppConfig
{
    public required Uri ApiBaseUrl { get; init; }

    public static AppConfig Load()
    {
        var url = "http://localhost:5080";
        foreach (var name in new[] { "appsettings.json", "appsettings.Development.json" })
            if (ReadBaseUrl(Path.Combine(AppContext.BaseDirectory, name)) is { } configured) url = configured;
        if (Environment.GetEnvironmentVariable("NERDY_API_URL") is { Length: > 0 } overridden) url = overridden;
        return new AppConfig { ApiBaseUrl = new Uri(url.TrimEnd('/') + "/") };
    }

    private static string? ReadBaseUrl(string file)
    {
        if (!File.Exists(file)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        return doc.RootElement.TryGetProperty("Api", out var api) &&
               api.TryGetProperty("BaseUrl", out var baseUrl) && baseUrl.GetString() is { Length: > 0 } configured
            ? configured
            : null;
    }
}
