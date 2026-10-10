using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.HttpOverrides;
using Nerdy.Api.Data;
using Nerdy.Api.Features.Auth;
using Nerdy.Api.Features.Classes;
using Nerdy.Api.Features.Focus;
using Nerdy.Api.Features.Me;
using Nerdy.Api.Features.Sync;
using Nerdy.Api.Infrastructure;
using Nerdy.Api.Infrastructure.Email;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddNerdyProblemDetails();
builder.Services.AddNerdyDatabase();
builder.AddNerdyAuth();
builder.Services.AddNerdyRateLimiting();
builder.Services.AddNerdyEmail();
builder.Services.AddNerdyFocus();
builder.Services.AddScoped<SyncService>();
builder.Services.AddOpenApi();

// Strict JSON: null for a non-nullable DTO member is a 400, not a NullReferenceException later.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.RespectNullableAnnotations = true);

// App Service terminates TLS and forwards the client address. The front end appends the real client IP last,
// and only the last hop is used (ForwardLimit = 1), so per-IP rate limiting cannot be spoofed.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

var app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
// After authentication so per-account policies (focus exit reviews) can see who is calling.
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var api = app.MapGroup("/api");
api.MapAuthEndpoints();
api.MapMeEndpoints();
api.MapSyncEndpoints();
api.MapClassesModule();
api.MapFocusEndpoints();
app.MapHealthChecks("/health");

await app.Services.InitializeDatabaseAsync();
await app.RunAsync();
