using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Options;

namespace Nerdy.Api.Infrastructure.Email;

public sealed record OutgoingEmail(string To, string Subject, string Html, string PlainText);

public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken);
}

public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>Azure Communication Services connection string. When empty, emails are saved to App_Data/emails instead.</summary>
    public string? AcsConnectionString { get; set; }
    /// <summary>Sender address on a domain verified in Azure Communication Services, e.g. DoNotReply@&lt;domain&gt;.</summary>
    public string? From { get; set; }
}

public static class EmailServiceCollectionExtensions
{
    public static IServiceCollection AddNerdyEmail(this IServiceCollection services)
    {
        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.Section);
        services.AddSingleton<IEmailSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<EmailOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.AcsConnectionString))
            {
                if (!sp.GetRequiredService<IHostEnvironment>().IsDevelopment())
                {
                    sp.GetRequiredService<ILogger<FileEmailSender>>().LogWarning(
                        "Email:AcsConnectionString is not configured; emails are saved to App_Data/emails instead of being sent");
                }

                return ActivatorUtilities.CreateInstance<FileEmailSender>(sp);
            }

            if (string.IsNullOrWhiteSpace(options.From))
            {
                throw new InvalidOperationException("Email:From is required when Email:AcsConnectionString is set.");
            }

            return new AcsEmailSender(
                new EmailClient(options.AcsConnectionString), options.From, sp.GetRequiredService<ILogger<AcsEmailSender>>());
        });
        return services;
    }
}

/// <summary>Sends through Azure Communication Services Email.</summary>
internal sealed class AcsEmailSender(EmailClient client, string from, ILogger<AcsEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        var message = new EmailMessage(from, email.To, new EmailContent(email.Subject)
        {
            Html = email.Html,
            PlainText = email.PlainText,
        });

        // WaitUntil.Started: ACS queues the message; delivery status is not worth holding the request open for.
        var operation = await client.SendAsync(WaitUntil.Started, message, cancellationToken);
        logger.LogInformation("Queued email {Subject} with Azure Communication Services (operation {OperationId})", email.Subject, operation.Id);
    }
}

/// <summary>
/// Development stand-in: writes each email to App_Data/emails/*.html so links can be clicked locally.
/// Only the recipient, subject and file are logged — never the body, which carries tokens.
/// </summary>
internal sealed class FileEmailSender(IHostEnvironment environment, TimeProvider time, ILogger<FileEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(environment.ContentRootPath, "App_Data", "emails");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{time.GetUtcNow():yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..8]}.html");
        var header = $"<!-- To: {email.To} | Subject: {email.Subject} -->{Environment.NewLine}";
        await File.WriteAllTextAsync(path, header + email.Html, cancellationToken);

        logger.LogInformation("Email {Subject} to {To} saved to {Path} (no email service configured)", email.Subject, email.To, path);
    }
}
