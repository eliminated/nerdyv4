using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nerdy.Contracts;

namespace Nerdy.App.Services.Auth;

/// <summary>A signed-in session as kept on disk.</summary>
public sealed record StoredSession
{
    public required string AccessToken { get; init; }
    public required DateTimeOffset AccessTokenExpiresAt { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTimeOffset RefreshTokenExpiresAt { get; init; }
    public required UserDto User { get; init; }

    public static StoredSession From(AuthResponse r) => new()
    {
        AccessToken = r.AccessToken,
        AccessTokenExpiresAt = r.AccessTokenExpiresAt,
        RefreshToken = r.RefreshToken,
        RefreshTokenExpiresAt = r.RefreshTokenExpiresAt,
        User = r.User
    };
}

/// <summary>
/// Persists the session encrypted with DPAPI (current Windows user scope), so tokens are unreadable to
/// other accounts on the PC and to anyone copying the file elsewhere.
/// </summary>
public sealed class TokenStore
{
    private static readonly byte[] Entropy = "Nerdy.Session.v1"u8.ToArray();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public StoredSession? Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SessionFile)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(AppPaths.SessionFile), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<StoredSession>(Encoding.UTF8.GetString(plain), Json);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            Clear();
            return null;
        }
    }

    public void Save(StoredSession session)
    {
        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(session, Json));
        var tmp = AppPaths.SessionFile + ".tmp";
        File.WriteAllBytes(tmp, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
        File.Move(tmp, AppPaths.SessionFile, overwrite: true);
    }

    public void Clear()
    {
        try { File.Delete(AppPaths.SessionFile); }
        catch (IOException) { /* best effort */ }
    }
}
