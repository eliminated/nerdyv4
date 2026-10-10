namespace Nerdy.Contracts;

public sealed record RegisterRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
    public required string DisplayName { get; init; }
}

public sealed record LoginRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}

public sealed record RefreshRequest
{
    public required string RefreshToken { get; init; }
}

public sealed record ForgotPasswordRequest
{
    public required string Email { get; init; }
}

public sealed record ResetPasswordRequest
{
    public required string Email { get; init; }
    public required string Token { get; init; }
    public required string NewPassword { get; init; }
}

/// <summary>Exchanges the one-time code from the external sign-in redirect for tokens (PKCE-style).</summary>
public sealed record ExternalExchangeRequest
{
    public required string Code { get; init; }
    public required string CodeVerifier { get; init; }
    public required string RedirectUri { get; init; }
}

public sealed record AuthResponse
{
    public required string AccessToken { get; init; }
    public required DateTimeOffset AccessTokenExpiresAt { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTimeOffset RefreshTokenExpiresAt { get; init; }
    public required UserDto User { get; init; }
}

public sealed record UserDto
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public UserRole Role { get; init; } = UserRole.Student;
    public bool OnboardingCompleted { get; init; }
    public bool EmailConfirmed { get; init; }
    public bool HasPassword { get; init; }
    public IReadOnlyList<string> ExternalProviders { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>Until onboarding is done: a role guessed from the email address for onboarding to pre-select (the person confirms it).</summary>
    public RoleSuggestionDto? RoleSuggestion { get; init; }
}

public sealed record RoleSuggestionDto
{
    public required UserRole Role { get; init; }
    public required RoleSuggestionSource Source { get; init; }
    /// <summary>The email's domain, shown back to the person ("uni.edu").</summary>
    public required string Domain { get; init; }
}

/// <summary>PATCH semantics: null members are left unchanged.</summary>
public sealed record UpdateProfileRequest
{
    public string? DisplayName { get; init; }
    public UserRole? Role { get; init; }
    public bool? OnboardingCompleted { get; init; }
}

public static class AuthProviders
{
    public const string Microsoft = "Microsoft";
    public const string Google = "Google";
}
