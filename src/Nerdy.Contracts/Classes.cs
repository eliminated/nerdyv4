namespace Nerdy.Contracts;

public sealed record ClassroomDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Subject { get; init; }
    public string ColorHex { get; init; } = "#5B5BD6";
    /// <summary>Only returned to the class's teacher.</summary>
    public string? JoinCode { get; init; }
    public required ClassRole MyRole { get; init; }
    public required string TeacherName { get; init; }
    public int StudentCount { get; init; }
    /// <summary>For students: whether they share their focus stats with this class.</summary>
    public bool SharesFocus { get; init; }
    public bool IsArchived { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record CreateClassroomRequest
{
    public required string Name { get; init; }
    public string? Subject { get; init; }
    public string? ColorHex { get; init; }
}

/// <summary>PATCH semantics: null members are left unchanged.</summary>
public sealed record UpdateClassroomRequest
{
    public string? Name { get; init; }
    public string? Subject { get; init; }
    public string? ColorHex { get; init; }
    public bool? IsArchived { get; init; }
}

public sealed record JoinClassroomRequest
{
    public required string JoinCode { get; init; }
}

public sealed record SetFocusSharingRequest
{
    public required bool Share { get; init; }
}

public sealed record ClassMemberDto
{
    public required Guid UserId { get; init; }
    public required string DisplayName { get; init; }
    /// <summary>Only visible to the teacher.</summary>
    public string? Email { get; init; }
    public required ClassRole Role { get; init; }
    public DateTimeOffset JoinedAt { get; init; }
    public bool SharesFocus { get; init; }
}

public sealed record AssignmentDto
{
    public required Guid Id { get; init; }
    public required Guid ClassroomId { get; init; }
    public required string ClassroomName { get; init; }
    public string ClassColorHex { get; init; } = "#5B5BD6";
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset DueAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>For students: whether they marked it done.</summary>
    public bool IsDone { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    /// <summary>For teachers: how many students have marked it done.</summary>
    public int CompletedCount { get; init; }
    public int StudentCount { get; init; }
}

public sealed record CreateAssignmentRequest
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset DueAt { get; init; }
}

/// <summary>PATCH semantics: null members are left unchanged.</summary>
public sealed record UpdateAssignmentRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset? DueAt { get; init; }
}

public sealed record SetAssignmentDoneRequest
{
    public required bool IsDone { get; init; }
}

public sealed record ClassProgressDto
{
    public required Guid ClassroomId { get; init; }
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyList<StudentProgressDto> Students { get; init; }
}

public sealed record StudentProgressDto
{
    public required Guid UserId { get; init; }
    public required string DisplayName { get; init; }
    public bool SharesFocus { get; init; }
    /// <summary>Null unless the student shares focus stats with this class.</summary>
    public int? FocusMinutes { get; init; }
    public int? Sessions { get; init; }
    /// <summary>0–100; null unless shared or no sessions.</summary>
    public int? CompletedAsPlannedPercent { get; init; }
    /// <summary>Focus minutes per day from <see cref="ClassProgressDto.From"/>; null unless shared.</summary>
    public IReadOnlyList<int>? DailyMinutes { get; init; }
    public int AssignmentsDone { get; init; }
    public int AssignmentsTotal { get; init; }
}
