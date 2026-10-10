using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nerdy.Contracts;

namespace Nerdy.App.Services.Api;

/// <summary>The server rejected a request; carries the RFC 7807 problem details.</summary>
public sealed class ApiException(HttpStatusCode status, string title, string? detail, IReadOnlyDictionary<string, string[]>? errors)
    : Exception(detail ?? title)
{
    public HttpStatusCode Status { get; } = status;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();

    /// <summary>A sentence suitable for showing to the user.</summary>
    public string UserMessage => Errors.Count > 0 ? string.Join(" ", Errors.Values.SelectMany(v => v)) : Detail ?? Title;
}

/// <summary>The server could not be reached (no network, API down, timeout).</summary>
public sealed class OfflineException(Exception inner) : Exception("Nerdy can't reach the server right now.", inner);

/// <summary>Supplies bearer tokens to <see cref="ApiClient"/>; implemented by <see cref="Auth.AuthService"/>.</summary>
public interface ISessionTokenSource
{
    Task<string?> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct);
}

/// <summary>Typed client for the Nerdy API (see docs/ARCHITECTURE.md for the surface).</summary>
public sealed class ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public ApiClient(AppConfig config)
    {
        // Never go through the system proxy: during a focus session the proxy is Nerdy's own website filter.
        var handler = new SocketsHttpHandler { UseProxy = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        _http = new HttpClient(handler) { BaseAddress = config.ApiBaseUrl, Timeout = TimeSpan.FromSeconds(30) };
        BaseAddress = config.ApiBaseUrl;
    }

    public Uri BaseAddress { get; }
    public ISessionTokenSource? Tokens { get; set; }

    // ----- Auth (anonymous) -----
    public Task<AuthResponse> RegisterAsync(RegisterRequest r, CancellationToken ct = default) => SendAsync<AuthResponse>(HttpMethod.Post, "api/auth/register", r, false, ct);
    public Task<AuthResponse> LoginAsync(LoginRequest r, CancellationToken ct = default) => SendAsync<AuthResponse>(HttpMethod.Post, "api/auth/login", r, false, ct);
    public Task<AuthResponse> RefreshAsync(RefreshRequest r, CancellationToken ct = default) => SendAsync<AuthResponse>(HttpMethod.Post, "api/auth/refresh", r, false, ct);
    public Task LogoutAsync(RefreshRequest r, CancellationToken ct = default) => SendAsync(HttpMethod.Post, "api/auth/logout", r, false, ct);
    public Task ForgotPasswordAsync(ForgotPasswordRequest r, CancellationToken ct = default) => SendAsync(HttpMethod.Post, "api/auth/forgot-password", r, false, ct);
    public Task ResetPasswordAsync(ResetPasswordRequest r, CancellationToken ct = default) => SendAsync(HttpMethod.Post, "api/auth/reset-password", r, false, ct);
    public Task<List<string>> GetProvidersAsync(CancellationToken ct = default) => SendAsync<List<string>>(HttpMethod.Get, "api/auth/providers", null, false, ct);
    public Task<AuthResponse> ExchangeExternalCodeAsync(ExternalExchangeRequest r, CancellationToken ct = default) => SendAsync<AuthResponse>(HttpMethod.Post, "api/auth/external/exchange", r, false, ct);
    public Uri ExternalStartUri(string provider, string redirectUri, string state, string codeChallenge) =>
        new(BaseAddress, $"api/auth/external/{Uri.EscapeDataString(provider)}/start?redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}&code_challenge={Uri.EscapeDataString(codeChallenge)}");

    // ----- Account -----
    public Task ResendConfirmationAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Post, "api/auth/resend-confirmation", null, true, ct);
    public Task<UserDto> GetMeAsync(CancellationToken ct = default) => SendAsync<UserDto>(HttpMethod.Get, "api/me", null, true, ct);
    public Task<UserDto> UpdateMeAsync(UpdateProfileRequest r, CancellationToken ct = default) => SendAsync<UserDto>(HttpMethod.Patch, "api/me", r, true, ct);
    public Task DeleteMeAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Delete, "api/me", null, true, ct);

    // ----- Sync -----
    public Task<SyncResponse> SyncAsync(SyncRequest r, CancellationToken ct = default) => SendAsync<SyncResponse>(HttpMethod.Post, "api/sync", r, true, ct);

    // ----- Focus -----
    public Task<ExitReviewResponse> ReviewExitAsync(ExitReviewRequest r, CancellationToken ct = default) => SendAsync<ExitReviewResponse>(HttpMethod.Post, "api/focus/exit-review", r, true, ct);

    // ----- Classes -----
    public Task<List<ClassroomDto>> GetClassesAsync(CancellationToken ct = default) => SendAsync<List<ClassroomDto>>(HttpMethod.Get, "api/classes", null, true, ct);
    public Task<ClassroomDto> CreateClassAsync(CreateClassroomRequest r, CancellationToken ct = default) => SendAsync<ClassroomDto>(HttpMethod.Post, "api/classes", r, true, ct);
    public Task<ClassroomDto> UpdateClassAsync(Guid id, UpdateClassroomRequest r, CancellationToken ct = default) => SendAsync<ClassroomDto>(HttpMethod.Patch, $"api/classes/{id}", r, true, ct);
    public Task DeleteClassAsync(Guid id, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, $"api/classes/{id}", null, true, ct);
    public Task<ClassroomDto> JoinClassAsync(JoinClassroomRequest r, CancellationToken ct = default) => SendAsync<ClassroomDto>(HttpMethod.Post, "api/classes/join", r, true, ct);
    public Task<ClassroomDto> RegenerateJoinCodeAsync(Guid id, CancellationToken ct = default) => SendAsync<ClassroomDto>(HttpMethod.Post, $"api/classes/{id}/join-code", null, true, ct);
    public Task<List<ClassMemberDto>> GetMembersAsync(Guid id, CancellationToken ct = default) => SendAsync<List<ClassMemberDto>>(HttpMethod.Get, $"api/classes/{id}/members", null, true, ct);
    public Task RemoveMemberAsync(Guid id, Guid userId, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, $"api/classes/{id}/members/{userId}", null, true, ct);
    public Task SetSharingAsync(Guid id, SetFocusSharingRequest r, CancellationToken ct = default) => SendAsync(HttpMethod.Put, $"api/classes/{id}/sharing", r, true, ct);
    public Task<List<AssignmentDto>> GetClassAssignmentsAsync(Guid id, CancellationToken ct = default) => SendAsync<List<AssignmentDto>>(HttpMethod.Get, $"api/classes/{id}/assignments", null, true, ct);
    public Task<AssignmentDto> CreateAssignmentAsync(Guid classId, CreateAssignmentRequest r, CancellationToken ct = default) => SendAsync<AssignmentDto>(HttpMethod.Post, $"api/classes/{classId}/assignments", r, true, ct);
    public Task<List<AssignmentDto>> GetMyAssignmentsAsync(CancellationToken ct = default) => SendAsync<List<AssignmentDto>>(HttpMethod.Get, "api/assignments", null, true, ct);
    public Task<AssignmentDto> UpdateAssignmentAsync(Guid id, UpdateAssignmentRequest r, CancellationToken ct = default) => SendAsync<AssignmentDto>(HttpMethod.Patch, $"api/assignments/{id}", r, true, ct);
    public Task DeleteAssignmentAsync(Guid id, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, $"api/assignments/{id}", null, true, ct);
    public Task SetAssignmentDoneAsync(Guid id, SetAssignmentDoneRequest r, CancellationToken ct = default) => SendAsync(HttpMethod.Put, $"api/assignments/{id}/done", r, true, ct);
    public Task<ClassProgressDto> GetProgressAsync(Guid id, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        SendAsync<ClassProgressDto>(HttpMethod.Get,
            $"api/classes/{id}/progress?from={Uri.EscapeDataString(from.ToString("o"))}&to={Uri.EscapeDataString(to.ToString("o"))}&utcOffsetMinutes={(int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes}",
            null, true, ct);

    // ----- Plumbing -----

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, bool authorized, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, path, body, authorized, ct);
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private async Task SendAsync(HttpMethod method, string path, object? body, bool authorized, CancellationToken ct)
    {
        using var _ = await SendCoreAsync(method, path, body, authorized, ct);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body, bool authorized, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);
            if (authorized)
            {
                var token = Tokens is null ? null : await Tokens.GetAccessTokenAsync(forceRefresh: attempt > 0, ct);
                if (token is null) throw new ApiException(HttpStatusCode.Unauthorized, "You're signed out.", "Please sign in again.", null);
                request.Headers.Authorization = new("Bearer", token);
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException ex) { throw new OfflineException(ex); }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new OfflineException(ex); }

            if (response.StatusCode == HttpStatusCode.Unauthorized && authorized && attempt == 0)
            {
                response.Dispose();
                continue; // refresh the access token once and retry
            }
            if (response.IsSuccessStatusCode) return response;

            using (response) throw await ToExceptionAsync(response, ct);
        }
    }

    private static async Task<ApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string title = response.ReasonPhrase ?? "Request failed";
        string? detail = null;
        Dictionary<string, string[]>? errors = null;
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            if (root.TryGetProperty("title", out var t) && t.GetString() is { } s) title = s;
            if (root.TryGetProperty("detail", out var d)) detail = d.GetString();
            if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
                errors = e.EnumerateObject().ToDictionary(
                    p => p.Name,
                    p => p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [p.Value.ToString()]);
        }
        catch (JsonException)
        {
            // Not a problem-details body; keep the status text.
        }
        return new ApiException(response.StatusCode, title, detail, errors);
    }
}
