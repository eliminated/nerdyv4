using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Nerdy.Api.Infrastructure.Email;

namespace Nerdy.Api.Tests.Infrastructure;

/// <summary>
/// The real API on an in-memory SQLite database. A named shared-cache database lets every DbContext open its own
/// connection; the factory holds one connection open for its lifetime so the database survives between requests.
/// </summary>
public class NerdyApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=nerdy-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;

    public NerdyApiFactory()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    public TestEmailSender Emails { get; } = new();

    /// <summary>High by default so tests are never throttled; <see cref="RateLimitedApiFactory"/> lowers it.</summary>
    protected virtual int AuthRequestsPerMinute => 100_000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" runs the production pipeline (HTTPS redirection, HSTS, no OpenAPI) with test configuration.
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Nerdy", _connectionString);
        builder.UseSetting("Jwt:SigningKey", "nerdy-test-signing-key-0123456789-0123456789");
        builder.UseSetting("RateLimits:AuthPerMinute", AuthRequestsPerMinute.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("App:PublicBaseUrl", "https://nerdy.test");
        // Google is configured (no network is touched until a real callback); Microsoft is not.
        builder.UseSetting("Authentication:Google:ClientId", "test-google-client-id");
        builder.UseSetting("Authentication:Google:ClientSecret", "test-google-client-secret");
        builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(Emails));
    }

    /// <summary>An HTTPS client (as on App Service) that does not follow redirects.</summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }
}

public sealed class RateLimitedApiFactory : NerdyApiFactory
{
    public const int Limit = 3;

    protected override int AuthRequestsPerMinute => Limit;
}

public sealed class TestEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<OutgoingEmail> _sent = new();

    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        _sent.Enqueue(email);
        return Task.CompletedTask;
    }

    public OutgoingEmail? LastTo(string address) => _sent.LastOrDefault(e => e.To == address);
}
