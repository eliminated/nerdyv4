using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Nerdy.Api.Tests.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Tests;

public sealed class ClassesTests(NerdyApiFactory factory) : IClassFixture<NerdyApiFactory>
{
    [Fact]
    public async Task A_student_cannot_create_a_class()
    {
        var student = await factory.RegisterAsync();
        var response = await student.Client.PostAsJsonAsync("/api/classes", new CreateClassroomRequest { Name = "Chemistry" });

        Assert.Equal(ApiErrorCodes.TeacherRequired, await response.ReadProblemCodeAsync(HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task A_teacher_creates_a_class_and_a_student_joins_with_the_code_in_any_case()
    {
        var teacher = await factory.RegisterTeacherAsync("Mr Curie");
        var created = await CreateClassAsync(teacher, "Chemistry");
        Assert.Equal(ClassRole.Teacher, created.MyRole);
        Assert.Equal("Mr Curie", created.TeacherName);
        Assert.Equal(7, created.JoinCode!.Length);
        Assert.DoesNotContain(created.JoinCode, c => "0O1IL".Contains(c));

        var student = await factory.RegisterAsync("Marie");
        var joined = await JoinAsync(student, created.JoinCode.ToLowerInvariant());

        Assert.Equal(created.Id, joined.Id);
        Assert.Equal(ClassRole.Student, joined.MyRole);
        Assert.Null(joined.JoinCode);
        Assert.Equal(1, joined.StudentCount);
        Assert.Equal("Mr Curie", joined.TeacherName);

        var teacherView = Assert.Single(await teacher.Client.GetAsync<List<ClassroomDto>>("/api/classes"));
        Assert.Equal(1, teacherView.StudentCount);
    }

    [Fact]
    public async Task Joining_twice_is_idempotent_and_a_teacher_cannot_join_their_own_class()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var classroom = await CreateClassAsync(teacher, "Maths");
        var student = await factory.RegisterAsync();

        await JoinAsync(student, classroom.JoinCode!);
        var again = await JoinAsync(student, classroom.JoinCode!);
        Assert.Equal(1, again.StudentCount);

        var own = await teacher.Client.PostAsJsonAsync("/api/classes/join", new JoinClassroomRequest { JoinCode = classroom.JoinCode! });
        Assert.Equal(ApiErrorCodes.AlreadyTeacher, await own.ReadProblemCodeAsync(HttpStatusCode.Conflict));

        var unknown = await student.Client.PostAsJsonAsync("/api/classes/join", new JoinClassroomRequest { JoinCode = "ZZZZZZZ" });
        await unknown.ReadProblemCodeAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Students_see_the_teacher_and_themselves_but_no_emails()
    {
        var teacher = await factory.RegisterTeacherAsync("Teacher");
        var classroom = await CreateClassAsync(teacher, "History");
        var alice = await factory.RegisterAsync("Alice");
        var bob = await factory.RegisterAsync("Bob");
        await JoinAsync(alice, classroom.JoinCode!);
        await JoinAsync(bob, classroom.JoinCode!);

        var studentView = await alice.Client.GetAsync<List<ClassMemberDto>>($"/api/classes/{classroom.Id}/members");
        Assert.Equal(["Teacher", "Alice"], studentView.Select(m => m.DisplayName));
        Assert.All(studentView, m => Assert.Null(m.Email));

        var teacherView = await teacher.Client.GetAsync<List<ClassMemberDto>>($"/api/classes/{classroom.Id}/members");
        Assert.Equal(3, teacherView.Count);
        Assert.Contains(teacherView, m => m.Email == bob.Email);

        var outsider = await factory.RegisterAsync();
        var hidden = await outsider.Client.GetAsync($"/api/classes/{classroom.Id}");
        await hidden.ReadProblemCodeAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Assignments_reach_students_and_completions_reach_the_teacher()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var classroom = await CreateClassAsync(teacher, "Biology");
        var student = await factory.RegisterAsync();
        var otherStudent = await factory.RegisterAsync();
        await JoinAsync(student, classroom.JoinCode!);
        await JoinAsync(otherStudent, classroom.JoinCode!);

        var due = new DateTimeOffset(2026, 11, 1, 23, 59, 0, TimeSpan.Zero);
        var created = await (await teacher.Client.PostAsJsonAsync($"/api/classes/{classroom.Id}/assignments",
            new CreateAssignmentRequest { Title = "Lab report", Description = "Photosynthesis", DueAt = due }))
            .ReadAsync<AssignmentDto>(HttpStatusCode.Created);
        Assert.Equal(2, created.StudentCount);

        var mine = Assert.Single(await student.Client.GetAsync<List<AssignmentDto>>("/api/assignments"));
        Assert.Equal("Lab report", mine.Title);
        Assert.Equal("Biology", mine.ClassroomName);
        Assert.Equal(due, mine.DueAt);
        Assert.False(mine.IsDone);

        var done = await (await student.Client.PutAsJsonAsync($"/api/assignments/{created.Id}/done", new SetAssignmentDoneRequest { IsDone = true }))
            .ReadAsync<AssignmentDto>();
        Assert.True(done.IsDone);
        Assert.NotNull(done.CompletedAt);

        var teacherView = Assert.Single(await teacher.Client.GetAsync<List<AssignmentDto>>($"/api/classes/{classroom.Id}/assignments"));
        Assert.Equal(1, teacherView.CompletedCount);
        Assert.Equal(2, teacherView.StudentCount);

        var studentEdit = await student.Client.PatchAsJsonAsync($"/api/assignments/{created.Id}", new UpdateAssignmentRequest { Title = "Easy" });
        Assert.Equal(ApiErrorCodes.NotClassTeacher, await studentEdit.ReadProblemCodeAsync(HttpStatusCode.Forbidden));

        var edited = await (await teacher.Client.PatchAsJsonAsync($"/api/assignments/{created.Id}", new UpdateAssignmentRequest { Title = "Lab report v2" }))
            .ReadAsync<AssignmentDto>();
        Assert.Equal("Lab report v2", edited.Title);
        Assert.Equal("Photosynthesis", edited.Description);

        var deleted = await teacher.Client.DeleteAsync($"/api/assignments/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(await student.Client.GetAsync<List<AssignmentDto>>("/api/assignments"));
    }

    [Fact]
    public async Task Progress_shows_focus_numbers_only_for_students_who_share()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var classroom = await CreateClassAsync(teacher, "Physics");
        var annUser = await factory.RegisterAsync("Ann");
        var benUser = await factory.RegisterAsync("Ben");
        await JoinAsync(annUser, classroom.JoinCode!);
        await JoinAsync(benUser, classroom.JoinCode!);

        var shared = await annUser.Client.PutAsJsonAsync($"/api/classes/{classroom.Id}/sharing", new SetFocusSharingRequest { Share = true });
        Assert.True((await shared.ReadAsync<ClassroomDto>()).SharesFocus);

        // Range: three local days in UTC+2, Oct 1-3. Sessions are bucketed by their local start day.
        var offset = TimeSpan.FromHours(2);
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, offset);
        var to = from.AddDays(3);
        await annUser.Client.SyncAsync(0, new SyncChanges
        {
            FocusSessions =
            [
                Session(from.AddHours(9), 1500, FocusOutcome.Completed),
                Session(from.AddHours(23.5), 600, FocusOutcome.EndedEarly),        // Oct 1, 23:30 local (21:30 UTC)
                Session(from.AddDays(2).AddHours(8), 3000, FocusOutcome.Completed),
                Session(from.AddDays(-1), 9000, FocusOutcome.Completed),           // before the range
                Session(from.AddHours(10), 9000, FocusOutcome.Completed) with { IsDeleted = true },
            ],
        });
        await benUser.Client.SyncAsync(0, new SyncChanges { FocusSessions = [Session(from.AddHours(9), 1200, FocusOutcome.Completed)] });

        var assignment = await (await teacher.Client.PostAsJsonAsync($"/api/classes/{classroom.Id}/assignments",
            new CreateAssignmentRequest { Title = "Problem set", DueAt = to })).ReadAsync<AssignmentDto>(HttpStatusCode.Created);
        await benUser.Client.PutAsJsonAsync($"/api/assignments/{assignment.Id}/done", new SetAssignmentDoneRequest { IsDone = true });

        var url = QueryHelpers.AddQueryString($"/api/classes/{classroom.Id}/progress", new Dictionary<string, string?>
        {
            ["from"] = from.ToString("O"),
            ["to"] = to.ToString("O"),
            ["utcOffsetMinutes"] = "120",
        });
        var progress = await teacher.Client.GetAsync<ClassProgressDto>(url);

        Assert.Equal(["Ann", "Ben"], progress.Students.Select(s => s.DisplayName));
        var ann = progress.Students[0];
        Assert.True(ann.SharesFocus);
        Assert.Equal(85, ann.FocusMinutes);
        Assert.Equal(3, ann.Sessions);
        Assert.Equal(67, ann.CompletedAsPlannedPercent);
        Assert.Equal([35, 0, 50], ann.DailyMinutes!);
        Assert.Equal((0, 1), (ann.AssignmentsDone, ann.AssignmentsTotal));

        var ben = progress.Students[1];
        Assert.False(ben.SharesFocus);
        Assert.Null(ben.FocusMinutes);
        Assert.Null(ben.Sessions);
        Assert.Null(ben.CompletedAsPlannedPercent);
        Assert.Null(ben.DailyMinutes);
        Assert.Equal((1, 1), (ben.AssignmentsDone, ben.AssignmentsTotal));

        var studentAsks = await annUser.Client.GetAsync($"/api/classes/{classroom.Id}/progress");
        Assert.Equal(ApiErrorCodes.NotClassTeacher, await studentAsks.ReadProblemCodeAsync(HttpStatusCode.Forbidden));

        var tooLong = await teacher.Client.GetAsync(QueryHelpers.AddQueryString($"/api/classes/{classroom.Id}/progress",
            new Dictionary<string, string?> { ["from"] = from.ToString("O"), ["to"] = from.AddDays(121).ToString("O") }));
        Assert.Contains("to", await tooLong.ReadValidationErrorsAsync());
    }

    [Fact]
    public async Task A_student_can_leave_and_a_teacher_can_remove_members_but_not_the_owner()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var classroom = await CreateClassAsync(teacher, "Art");
        var leaver = await factory.RegisterAsync();
        var removed = await factory.RegisterAsync();
        await JoinAsync(leaver, classroom.JoinCode!);
        await JoinAsync(removed, classroom.JoinCode!);

        var kick = await leaver.Client.DeleteAsync($"/api/classes/{classroom.Id}/members/{removed.Id}");
        Assert.Equal(ApiErrorCodes.NotClassTeacher, await kick.ReadProblemCodeAsync(HttpStatusCode.Forbidden));

        var leave = await leaver.Client.DeleteAsync($"/api/classes/{classroom.Id}/members/{leaver.Id}");
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        Assert.Empty(await leaver.Client.GetAsync<List<ClassroomDto>>("/api/classes"));

        var remove = await teacher.Client.DeleteAsync($"/api/classes/{classroom.Id}/members/{removed.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var afterRemove = await removed.Client.GetAsync($"/api/classes/{classroom.Id}");
        await afterRemove.ReadProblemCodeAsync(HttpStatusCode.NotFound);

        var ownerLeaves = await teacher.Client.DeleteAsync($"/api/classes/{classroom.Id}/members/{teacher.Id}");
        Assert.Equal(ApiErrorCodes.OwnerCannotLeave, await ownerLeaves.ReadProblemCodeAsync(HttpStatusCode.Conflict));

        Assert.Equal(0, (await teacher.Client.GetAsync<ClassroomDto>($"/api/classes/{classroom.Id}")).StudentCount);
    }

    [Fact]
    public async Task An_archived_class_is_read_only_and_drops_out_of_the_assignment_feed()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var classroom = await CreateClassAsync(teacher, "Latin");
        var student = await factory.RegisterAsync();
        await JoinAsync(student, classroom.JoinCode!);
        var assignment = await (await teacher.Client.PostAsJsonAsync($"/api/classes/{classroom.Id}/assignments",
            new CreateAssignmentRequest { Title = "Translate", DueAt = DateTimeOffset.UtcNow.AddDays(7) })).ReadAsync<AssignmentDto>(HttpStatusCode.Created);

        var archived = await (await teacher.Client.PatchAsJsonAsync($"/api/classes/{classroom.Id}", new UpdateClassroomRequest { IsArchived = true }))
            .ReadAsync<ClassroomDto>();
        Assert.True(archived.IsArchived);
        Assert.Equal("Latin", archived.Name);

        Assert.True(Assert.Single(await student.Client.GetAsync<List<ClassroomDto>>("/api/classes")).IsArchived);
        Assert.Empty(await student.Client.GetAsync<List<AssignmentDto>>("/api/assignments"));
        Assert.Single(await student.Client.GetAsync<List<AssignmentDto>>($"/api/classes/{classroom.Id}/assignments"));

        var newcomer = await factory.RegisterAsync();
        var join = await newcomer.Client.PostAsJsonAsync("/api/classes/join", new JoinClassroomRequest { JoinCode = classroom.JoinCode! });
        Assert.Equal(ApiErrorCodes.ClassArchived, await join.ReadProblemCodeAsync(HttpStatusCode.Conflict));

        var create = await teacher.Client.PostAsJsonAsync($"/api/classes/{classroom.Id}/assignments",
            new CreateAssignmentRequest { Title = "More", DueAt = DateTimeOffset.UtcNow });
        Assert.Equal(ApiErrorCodes.ClassArchived, await create.ReadProblemCodeAsync(HttpStatusCode.Conflict));

        var done = await student.Client.PutAsJsonAsync($"/api/assignments/{assignment.Id}/done", new SetAssignmentDoneRequest { IsDone = true });
        Assert.Equal(ApiErrorCodes.ClassArchived, await done.ReadProblemCodeAsync(HttpStatusCode.Conflict));

        var restored = await (await teacher.Client.PatchAsJsonAsync($"/api/classes/{classroom.Id}", new UpdateClassroomRequest { IsArchived = false }))
            .ReadAsync<ClassroomDto>();
        Assert.False(restored.IsArchived);
        Assert.Single(await student.Client.GetAsync<List<AssignmentDto>>("/api/assignments"));
    }

    [Fact]
    public async Task Regenerating_the_join_code_retires_the_old_one()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var classroom = await CreateClassAsync(teacher, "Music");

        var regenerated = await (await teacher.Client.PostAsync($"/api/classes/{classroom.Id}/join-code", null)).ReadAsync<ClassroomDto>();
        Assert.NotEqual(classroom.JoinCode, regenerated.JoinCode);

        var student = await factory.RegisterAsync();
        var old = await student.Client.PostAsJsonAsync("/api/classes/join", new JoinClassroomRequest { JoinCode = classroom.JoinCode! });
        await old.ReadProblemCodeAsync(HttpStatusCode.NotFound);
        await JoinAsync(student, regenerated.JoinCode!);
    }

    [Fact]
    public async Task Deleting_a_class_removes_it_for_everyone()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var classroom = await CreateClassAsync(teacher, "Drama");
        var student = await factory.RegisterAsync();
        await JoinAsync(student, classroom.JoinCode!);

        var studentDelete = await student.Client.DeleteAsync($"/api/classes/{classroom.Id}");
        Assert.Equal(ApiErrorCodes.NotClassTeacher, await studentDelete.ReadProblemCodeAsync(HttpStatusCode.Forbidden));

        var delete = await teacher.Client.DeleteAsync($"/api/classes/{classroom.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(await student.Client.GetAsync<List<ClassroomDto>>("/api/classes"));
    }

    private static async Task<ClassroomDto> CreateClassAsync(TestUser teacher, string name) =>
        await (await teacher.Client.PostAsJsonAsync("/api/classes", new CreateClassroomRequest { Name = name, Subject = "Science", ColorHex = "#336699" }))
            .ReadAsync<ClassroomDto>(HttpStatusCode.Created);

    private static async Task<ClassroomDto> JoinAsync(TestUser student, string code) =>
        await (await student.Client.PostAsJsonAsync("/api/classes/join", new JoinClassroomRequest { JoinCode = code })).ReadAsync<ClassroomDto>();

    private static FocusSessionDto Session(DateTimeOffset startedAt, int seconds, FocusOutcome outcome) => new()
    {
        Id = Guid.NewGuid(),
        UpdatedAt = startedAt,
        Task = "Study",
        PlannedSeconds = 1500,
        ActualSeconds = seconds,
        StartedAt = startedAt,
        EndedAt = startedAt.AddSeconds(seconds),
        Outcome = outcome,
    };
}
