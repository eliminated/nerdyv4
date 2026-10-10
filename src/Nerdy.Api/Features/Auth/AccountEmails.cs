using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Nerdy.Api.Data.Entities;
using Nerdy.Api.Infrastructure;
using Nerdy.Api.Infrastructure.Email;

namespace Nerdy.Api.Features.Auth;

/// <summary>
/// Account emails. Identity tokens are base64url-encoded for links and for pasting into the app; sending failures
/// are logged and swallowed so they never fail (or reveal anything through) the request that triggered them.
/// </summary>
public sealed class AccountEmails(
    UserManager<AppUser> users,
    IEmailSender sender,
    IOptions<AppOptions> appOptions,
    ILogger<AccountEmails> logger)
{
    public async Task SendConfirmationAsync(AppUser user, HttpRequest request, CancellationToken cancellationToken)
    {
        var token = EncodeToken(await users.GenerateEmailConfirmationTokenAsync(user));
        var link = $"{appOptions.Value.ResolveBaseUrl(request)}/api/auth/confirm-email" +
                   $"?userId={user.Id}&token={Uri.EscapeDataString(token)}";

        await SendAsync(user, "Confirm your Nerdy email", $"""
            <p>Hi {Html(user.DisplayName)},</p>
            <p>Welcome to Nerdy! Please confirm your email address:</p>
            <p><a href="{Html(link)}">Confirm my email</a></p>
            <p>If you didn't create a Nerdy account, you can ignore this email.</p>
            """, $"Hi {user.DisplayName},\n\nConfirm your Nerdy email address by opening this link:\n{link}\n", cancellationToken);
    }

    public async Task SendPasswordResetAsync(AppUser user, HttpRequest request, CancellationToken cancellationToken)
    {
        var token = EncodeToken(await users.GeneratePasswordResetTokenAsync(user));
        // Fragment, not query: the page reads it in the browser, so the token stays out of request logs.
        var link = $"{appOptions.Value.ResolveBaseUrl(request)}/api/auth/reset-password" +
                   $"#email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";

        await SendAsync(user, "Reset your Nerdy password", $"""
            <p>Hi {Html(user.DisplayName)},</p>
            <p>Someone (hopefully you) asked to reset your Nerdy password.</p>
            <p><a href="{Html(link)}">Choose a new password</a></p>
            <p>Or paste this reset code into Nerdy:</p>
            <p style="font-family: Consolas, monospace; word-break: break-all;">{Html(token)}</p>
            <p>The link and code work for 24 hours. If you didn't ask for this, you can ignore this email.</p>
            """, $"Hi {user.DisplayName},\n\nChoose a new Nerdy password here:\n{link}\n\nOr paste this reset code into Nerdy:\n{token}\n", cancellationToken);
    }

    /// <summary>Reverses the encoding used in emails; null when the value is not valid base64url.</summary>
    public static string? DecodeToken(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded.Trim()));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string EncodeToken(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private async Task SendAsync(AppUser user, string subject, string html, string text, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(new OutgoingEmail(user.Email!, subject, html, text), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Sending email {Subject} to user {UserId} failed", subject, user.Id);
        }
    }

    private static string Html(string value) => HtmlEncoder.Default.Encode(value);
}
