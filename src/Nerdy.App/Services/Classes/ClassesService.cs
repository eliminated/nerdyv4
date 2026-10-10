using System.Text.Json;
using Nerdy.App.Services.Api;
using Nerdy.App.Services.Data;
using Nerdy.Contracts;

namespace Nerdy.App.Services.Classes;

/// <summary>
/// Classes and assignments (server-authoritative). The last successful response is cached in the local store so
/// students still see their class assignments in Reminders and Calendar while offline. Use from the UI thread.
/// </summary>
public sealed class ClassesService(ApiClient api, DataService data)
{
    private const string ClassesKey = "cache.classes";
    private const string AssignmentsKey = "cache.assignments";

    public IReadOnlyList<ClassroomDto> Classes { get; private set; } = [];

    /// <summary>Assignments from every class the user is a student in.</summary>
    public IReadOnlyList<AssignmentDto> MyAssignments { get; private set; } = [];

    public bool IsLoaded { get; private set; }

    /// <summary>Raised on the UI thread whenever <see cref="Classes"/> or <see cref="MyAssignments"/> change.</summary>
    public event EventHandler? Changed;

    public async Task LoadCacheAsync()
    {
        Classes = Read<List<ClassroomDto>>(await data.GetMetaAsync(ClassesKey)) ?? [];
        MyAssignments = Read<List<AssignmentDto>>(await data.GetMetaAsync(AssignmentsKey)) ?? [];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        Classes = [];
        MyAssignments = [];
        IsLoaded = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Fetches classes and the student's assignments. Returns false when offline (cache stays).</summary>
    public async Task<bool> RefreshAsync()
    {
        try
        {
            var classes = await api.GetClassesAsync();
            var assignments = classes.Any(c => c.MyRole == ClassRole.Student) ? await api.GetMyAssignmentsAsync() : [];
            Classes = classes;
            MyAssignments = assignments;
            IsLoaded = true;
            await data.SetMetaAsync(ClassesKey, JsonSerializer.Serialize(classes, DataService.Json));
            await data.SetMetaAsync(AssignmentsKey, JsonSerializer.Serialize(assignments, DataService.Json));
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (OfflineException)
        {
            return false;
        }
    }

    public async Task<ClassroomDto> CreateAsync(string name, string? subject, string colorHex)
    {
        var created = await api.CreateClassAsync(new CreateClassroomRequest { Name = name.Trim(), Subject = Blank(subject), ColorHex = colorHex });
        await RefreshAsync();
        return created;
    }

    public async Task<ClassroomDto> JoinAsync(string code)
    {
        var joined = await api.JoinClassAsync(new JoinClassroomRequest { JoinCode = code.Trim() });
        await RefreshAsync();
        return joined;
    }

    public async Task<ClassroomDto> UpdateAsync(Guid id, UpdateClassroomRequest request)
    {
        var updated = await api.UpdateClassAsync(id, request);
        await RefreshAsync();
        return updated;
    }

    public async Task DeleteAsync(Guid id)
    {
        await api.DeleteClassAsync(id);
        await RefreshAsync();
    }

    public async Task<ClassroomDto> RegenerateCodeAsync(Guid id)
    {
        var updated = await api.RegenerateJoinCodeAsync(id);
        await RefreshAsync();
        return updated;
    }

    public Task<List<ClassMemberDto>> MembersAsync(Guid id) => api.GetMembersAsync(id);

    public async Task RemoveMemberAsync(Guid classId, Guid userId)
    {
        await api.RemoveMemberAsync(classId, userId);
        await RefreshAsync();
    }

    public async Task SetSharingAsync(Guid classId, bool share)
    {
        await api.SetSharingAsync(classId, new SetFocusSharingRequest { Share = share });
        await RefreshAsync();
    }

    public Task<List<AssignmentDto>> AssignmentsAsync(Guid classId) => api.GetClassAssignmentsAsync(classId);

    public async Task<AssignmentDto> CreateAssignmentAsync(Guid classId, string title, string? description, DateTimeOffset dueAt) =>
        await api.CreateAssignmentAsync(classId, new CreateAssignmentRequest { Title = title.Trim(), Description = Blank(description), DueAt = dueAt });

    public Task<AssignmentDto> UpdateAssignmentAsync(Guid id, UpdateAssignmentRequest request) => api.UpdateAssignmentAsync(id, request);

    public Task DeleteAssignmentAsync(Guid id) => api.DeleteAssignmentAsync(id);

    /// <summary>Marks an assignment done for the student; updates the cache optimistically and rolls back on failure.</summary>
    public async Task SetDoneAsync(Guid assignmentId, bool done)
    {
        var before = MyAssignments;
        MyAssignments = MyAssignments.Select(a => a.Id == assignmentId ? a with { IsDone = done, CompletedAt = done ? DateTimeOffset.Now : null } : a).ToList();
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await api.SetAssignmentDoneAsync(assignmentId, new SetAssignmentDoneRequest { IsDone = done });
            await data.SetMetaAsync(AssignmentsKey, JsonSerializer.Serialize(MyAssignments, DataService.Json));
        }
        catch
        {
            MyAssignments = before;
            Changed?.Invoke(this, EventArgs.Empty);
            throw;
        }
    }

    public Task<ClassProgressDto> ProgressAsync(Guid classId, DateTimeOffset from, DateTimeOffset to) => api.GetProgressAsync(classId, from, to);

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, DataService.Json); }
        catch (JsonException) { return null; }
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
