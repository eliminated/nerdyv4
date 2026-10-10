using Nerdy.Contracts;

namespace Nerdy.Api.Data.Entities;

// Teacher features. Server-authoritative: these are not synced, the app caches API responses for offline viewing.

public sealed class Classroom
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>The teacher who created the class; deleting that account deletes the class.</summary>
    public Guid OwnerId { get; set; }
    public AppUser Owner { get; set; } = null!;
    public required string Name { get; set; }
    public string? Subject { get; set; }
    public required string ColorHex { get; set; }
    /// <summary>Stored upper-case; matched case-insensitively by normalizing the input.</summary>
    public required string JoinCode { get; set; }
    /// <summary>Archived classes are read-only: no joins, no new or edited assignments, no completions.</summary>
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<ClassMembership> Members { get; set; } = [];
    public List<Assignment> Assignments { get; set; } = [];
}

/// <summary>
/// Everyone in a class, including its teacher (<see cref="ClassRole.Teacher"/>), so co-teachers or other roles
/// can be added later without a schema change.
/// </summary>
public sealed class ClassMembership
{
    public Guid ClassroomId { get; set; }
    public Classroom Classroom { get; set; } = null!;
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public ClassRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    /// <summary>Student opt-in: share focus statistics with this class's teacher.</summary>
    public bool SharesFocus { get; set; }
}

public sealed class Assignment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClassroomId { get; set; }
    public Classroom Classroom { get; set; } = null!;
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>
    /// Informational, not a foreign key: SQL Server rejects a second cascade path from users to assignments,
    /// and the assignment is deleted with its class anyway.
    /// </summary>
    public Guid CreatedById { get; set; }

    public List<AssignmentCompletion> Completions { get; set; } = [];
}

public sealed class AssignmentCompletion
{
    public Guid AssignmentId { get; set; }
    public Assignment Assignment { get; set; } = null!;
    public Guid UserId { get; set; }
    public bool IsDone { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
