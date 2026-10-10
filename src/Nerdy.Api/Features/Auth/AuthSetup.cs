using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nerdy.Api.Data;
using Nerdy.Api.Data.Entities;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Auth;

public static class AuthSetup
{
    /// <summary>Short-lived cookie that carries the provider's result from /signin-{provider} to our callback.</summary>
    public const string ExternalScheme = "External";

    public static WebApplicationBuilder AddNerdyAuth(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        // Fail fast at startup (every environment; Development gets a key from appsettings.Development.json).
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddOptions<Infrastructure.AppOptions>().BindConfiguration(Infrastructure.AppOptions.Section);

        // Data protection backs the External cookie, OAuth state and Identity's email/reset tokens. On App Service
        // keys persist under %HOME%/ASP.NET/DataProtection-Keys, shared by all instances.
        services.AddDataProtection().SetApplicationName("Nerdy.Api");

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = ""; // User names are email addresses, which may contain any character.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.SignIn.RequireConfirmedEmail = false; // The app shows a banner instead (UserDto.EmailConfirmed).
            })
            .AddEntityFrameworkStores<NerdyDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        var authentication = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddCookie(ExternalScheme, options =>
            {
                options.Cookie.Name = "nerdy.external";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            });

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false; // Keep "sub", "email", "name", "role" as issued.
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Value.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = jwt.Value.CreateSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Name,
                    RoleClaimType = TokenService.RoleClaim,
                };
            });

        // External providers exist only when configured, so /api/auth/providers reflects reality.
        if (!string.IsNullOrWhiteSpace(configuration["Authentication:Microsoft:ClientId"]))
        {
            // The default "common" endpoints accept personal Microsoft accounts and work/school accounts.
            authentication.AddMicrosoftAccount(AuthProviders.Microsoft, options =>
            {
                configuration.GetSection("Authentication:Microsoft").Bind(options);
                options.SignInScheme = ExternalScheme;
                options.ClaimActions.MapJsonKey(ExternalClaims.UserPrincipalName, "userPrincipalName");
                ExternalAuthEndpoints.HandleProviderErrors(options.Events);
            });
        }

        if (!string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]))
        {
            authentication.AddGoogle(AuthProviders.Google, options =>
            {
                configuration.GetSection("Authentication:Google").Bind(options);
                options.SignInScheme = ExternalScheme;
                options.ClaimActions.MapJsonKey(ExternalClaims.EmailVerified, "email_verified");
                ExternalAuthEndpoints.HandleProviderErrors(options.Events);
            });
        }

        services.AddAuthorization();
        services.AddScoped<TokenService>();
        services.AddScoped<ExternalLoginService>();
        services.AddScoped<AccountEmails>();
        return builder;
    }

    /// <summary>The external providers registered at startup, in a stable order.</summary>
    public static async Task<IReadOnlyList<string>> GetConfiguredProvidersAsync(this IAuthenticationSchemeProvider schemes)
    {
        var registered = (await schemes.GetAllSchemesAsync()).Select(s => s.Name).ToHashSet();
        return new[] { AuthProviders.Microsoft, AuthProviders.Google }.Where(registered.Contains).ToList();
    }
}
