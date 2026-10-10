using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Nerdy.Api.Data;

namespace Nerdy.Api.Features.Classes;

/// <summary>Class join codes: 7 characters a student can read off a whiteboard and type without ambiguity.</summary>
public static class JoinCodes
{
    public const int Length = 7;

    /// <summary>A–Z and 2–9 without 0/O, 1/I/L (31 symbols, ~27 billion codes).</summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int MaxAttempts = 10;

    /// <summary>Upper-cases and drops spaces/dashes, so "abc-d23 4" matches "ABCD234".</summary>
    public static string Normalize(string? input) =>
        new((input ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static async Task<string> NewUniqueAsync(NerdyDbContext db, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var code = RandomNumberGenerator.GetString(Alphabet, Length);
            if (!await db.Classrooms.AnyAsync(c => c.JoinCode == code, cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not generate a unique join code.");
    }
}
