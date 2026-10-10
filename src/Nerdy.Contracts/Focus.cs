namespace Nerdy.Contracts;

/// <summary>
/// Asks the server whether an Ultra-Focus emergency exit should go ahead. Only the non-emergency reasons
/// (<see cref="ExitReason.Urgent"/>, <see cref="ExitReason.Other"/>) are ever reviewed; medical and family
/// emergencies always exit immediately and are never sent.
/// </summary>
public sealed record ExitReviewRequest
{
    public required ExitReason Reason { get; init; }
    public required string Explanation { get; init; }
    public string? Task { get; init; }
    public int MinutesLeft { get; init; }
    public int PlannedMinutes { get; init; }
}

public sealed record ExitReviewResponse
{
    /// <summary>True when the user may leave now.</summary>
    public required bool Allowed { get; init; }
    /// <summary>A short, supportive message to show the user (why to stay, or an acknowledgement).</summary>
    public string? Message { get; init; }
    /// <summary>False when no review happened (not configured, unavailable, or not a reviewable reason): the exit is allowed.</summary>
    public bool Reviewed { get; init; }
}
