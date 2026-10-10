using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Nerdy.Api.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in user's id from the access token's <c>sub</c> claim (endpoints using this require authorization).</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("The access token has no 'sub' claim."));
}
