namespace Nerdy.Contracts;

/// <summary>
/// Machine-readable reasons the API puts in the <c>code</c> extension member of its RFC 7807 problem details,
/// so the app can branch or localize without parsing <c>title</c>/<c>detail</c>. Problems produced by the
/// framework itself (401 without a token, 404 for unknown routes, ...) carry no code.
/// </summary>
public static class ApiErrorCodes
{
    /// <summary>400 with an <c>errors</c> map of field path → messages.</summary>
    public const string ValidationFailed = "validation_failed";

    public const string InvalidCredentials = "invalid_credentials";
    public const string LockedOut = "locked_out";
    public const string EmailTaken = "email_taken";
    /// <summary>Refresh token unknown, expired, revoked or reused: sign the user out.</summary>
    public const string InvalidRefreshToken = "invalid_refresh_token";
    public const string InvalidResetToken = "invalid_reset_token";
    public const string ProviderNotConfigured = "provider_not_configured";
    public const string InvalidRedirectUri = "invalid_redirect_uri";
    /// <summary>External sign-in code unknown, used, expired, or verifier / redirect URI mismatch.</summary>
    public const string InvalidCode = "invalid_code";

    /// <summary>Concurrent syncs of the same account kept colliding; retry shortly.</summary>
    public const string SyncConflict = "sync_conflict";

    public const string NotFound = "not_found";
    public const string TeacherRequired = "teacher_required";
    public const string NotClassTeacher = "not_class_teacher";
    public const string StudentsOnly = "students_only";
    public const string AlreadyTeacher = "already_teacher";
    public const string ClassArchived = "class_archived";
    public const string OwnerCannotLeave = "owner_cannot_leave";

    public const string RateLimited = "rate_limited";
}
