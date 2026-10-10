using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerdy.Api.Data;
using Nerdy.Api.Tests.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Tests;

public sealed class AccountTests(NerdyApiFactory factory) : IClassFixture<NerdyApiFactory>
{
    [Fact]
    public async Task Patch_me_updates_only_the_given_fields()
    {
        var user = await factory.RegisterAsync("Before");

        var updated = await (await user.Client.PatchAsJsonAsync("/api/me", new UpdateProfileRequest { OnboardingCompleted = true, Role = UserRole.Learner }))
            .ReadAsync<UserDto>();
        Assert.Equal("Before", updated.DisplayName);
        Assert.Equal(UserRole.Learner, updated.Role);
        Assert.True(updated.OnboardingCompleted);

        var blank = await user.Client.PatchAsJsonAsync("/api/me", new UpdateProfileRequest { DisplayName = "  " });
        Assert.Contains("displayName", await blank.ReadValidationErrorsAsync());
    }

    [Fact]
    public async Task Delete_me_removes_the_account_and_everything_it_owns()
    {
        var teacher = await factory.RegisterTeacherAsync();
        var student = await factory.RegisterAsync();

        // The teacher owns a class with a student, an assignment and a completion; the teacher is also a student
        // in someone else's class, and has synced data and sessions.
        var ownClass = await (await teacher.Client.PostAsJsonAsync("/api/classes", new CreateClassroomRequest { Name = "Owned" }))
            .ReadAsync<ClassroomDto>(HttpStatusCode.Created);
        await student.Client.PostAsJsonAsync("/api/classes/join", new JoinClassroomRequest { JoinCode = ownClass.JoinCode! });
        var assignment = await (await teacher.Client.PostAsJsonAsync($"/api/classes/{ownClass.Id}/assignments",
            new CreateAssignmentRequest { Title = "Essay", DueAt = DateTimeOffset.UtcNow.AddDays(3) })).ReadAsync<AssignmentDto>(HttpStatusCode.Created);
        await student.Client.PutAsJsonAsync($"/api/assignments/{assignment.Id}/done", new SetAssignmentDoneRequest { IsDone = true });

        var otherTeacher = await factory.RegisterTeacherAsync();
        var otherClass = await (await otherTeacher.Client.PostAsJsonAsync("/api/classes", new CreateClassroomRequest { Name = "Other" }))
            .ReadAsync<ClassroomDto>(HttpStatusCode.Created);
        await teacher.Client.PostAsJsonAsync("/api/classes/join", new JoinClassroomRequest { JoinCode = otherClass.JoinCode! });
        var otherAssignment = await (await otherTeacher.Client.PostAsJsonAsync($"/api/classes/{otherClass.Id}/assignments",
            new CreateAssignmentRequest { Title = "Quiz", DueAt = DateTimeOffset.UtcNow.AddDays(3) })).ReadAsync<AssignmentDto>(HttpStatusCode.Created);
        await teacher.Client.PutAsJsonAsync($"/api/assignments/{otherAssignment.Id}/done", new SetAssignmentDoneRequest { IsDone = true });

        await teacher.Client.SyncAsync(0, new SyncChanges
        {
            Topics = [new TopicDto { Id = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow, Name = "Mine", ColorHex = "#112233" }],
            Preferences = new PreferencesDto { UpdatedAt = DateTimeOffset.UtcNow, Json = "{}" },
        });

        var delete = await teacher.Client.DeleteAsync("/api/me");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NerdyDbContext>();
        var id = teacher.Id;
        Assert.False(await db.Users.AnyAsync(u => u.Id == id));
        Assert.False(await db.Topics.AnyAsync(t => t.UserId == id));
        Assert.False(await db.Preferences.AnyAsync(p => p.UserId == id));
        Assert.False(await db.SyncStates.AnyAsync(s => s.UserId == id));
        Assert.False(await db.RefreshTokens.AnyAsync(t => t.UserId == id));
        Assert.False(await db.Classrooms.AnyAsync(c => c.Id == ownClass.Id));
        Assert.False(await db.Assignments.AnyAsync(a => a.Id == assignment.Id));
        Assert.False(await db.AssignmentCompletions.AnyAsync(c => c.AssignmentId == assignment.Id));
        Assert.False(await db.ClassMemberships.AnyAsync(m => m.UserId == id || m.ClassroomId == ownClass.Id));
        Assert.False(await db.AssignmentCompletions.AnyAsync(c => c.UserId == id));

        // Other people's classes survive; the student simply no longer sees the deleted one.
        Assert.True(await db.Classrooms.AnyAsync(c => c.Id == otherClass.Id));
        Assert.Empty(await student.Client.GetAsync<List<ClassroomDto>>("/api/classes"));

        var login = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = teacher.Email, Password = teacher.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}
