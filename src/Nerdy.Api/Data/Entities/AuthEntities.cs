namespace Nerdy.Api.Data.Entities;

/// <summary>
/// A rotating refresh token. Only the SHA-256 hash is stored. Every refresh revokes the presented token and
/// issues a successor in the same <see cref="FamilyId"/> (one family per sign-in on one device); presenting a
/// token that is already revoked revokes the whole family.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public Guid FamilyId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    /// <summary>Hash of the token that replaced this one on rotation (null when revoked by logout or reuse detection).</summary>
    public string? ReplacedByHash { get; set; }
}

/// <summary>
/// The single-use code handed to the desktop app's loopback redirect after an external sign-in
/// (RFC 8252 + PKCE-style). Only the hash is stored.
/// </summary>
public sealed class ExternalLoginCode
{
    public required string CodeHash { get; set; }
    public Guid UserId { get; set; }
    /// <summary>base64url(SHA-256(code_verifier)) as sent to the start endpoint.</summary>
    public required string CodeChallenge { get; set; }
    public required string RedirectUri { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}
