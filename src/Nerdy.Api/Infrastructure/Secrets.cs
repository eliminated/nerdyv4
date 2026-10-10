using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Nerdy.Api.Infrastructure;

/// <summary>Random bearer secrets (refresh tokens, one-time codes) and the hashes we store instead of them.</summary>
public static class Secrets
{
    /// <summary>A cryptographically random secret encoded as unpadded base64url.</summary>
    public static string NewToken(int byteCount) =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));

    /// <summary>base64url(SHA-256(UTF-8(value))): the stored form of a secret, and the PKCE S256 transform.</summary>
    public static string Hash(string value) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
