using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Nerdy.Api.Features.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    /// <summary>HMAC-SHA256 key, at least 32 bytes as UTF-8. Lives in Key Vault in production.</summary>
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    /// <summary>The first configuration problem, or null when the options are usable.</summary>
    public string? FindProblem() =>
        Encoding.UTF8.GetByteCount(SigningKey) < MinimumKeyBytes ? $"Jwt:SigningKey must be at least {MinimumKeyBytes} bytes."
        // App Service passes an unresolvable Key Vault reference through verbatim; never sign with that text.
        : SigningKey.StartsWith("@Microsoft.KeyVault(", StringComparison.OrdinalIgnoreCase) ? "Jwt:SigningKey is an unresolved Key Vault reference."
        : string.IsNullOrWhiteSpace(Issuer) ? "Jwt:Issuer is required."
        : string.IsNullOrWhiteSpace(Audience) ? "Jwt:Audience is required."
        : AccessTokenMinutes <= 0 || RefreshTokenDays <= 0 ? "Jwt:AccessTokenMinutes and Jwt:RefreshTokenDays must be positive."
        : null;
}

internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options) =>
        options.FindProblem() is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
}
