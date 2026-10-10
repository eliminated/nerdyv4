namespace Nerdy.Api.Infrastructure;

public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>
    /// Public origin of the API (e.g. https://nerdy-api.azurewebsites.net) used in email links.
    /// When empty, the origin of the current request is used.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    public string ResolveBaseUrl(HttpRequest request) =>
        string.IsNullOrWhiteSpace(PublicBaseUrl)
            ? $"{request.Scheme}://{request.Host}{request.PathBase}"
            : PublicBaseUrl.TrimEnd('/');
}
