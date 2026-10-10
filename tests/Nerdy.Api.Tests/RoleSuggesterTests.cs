using System.Net.Http.Json;
using Nerdy.Api.Features.Me;
using Nerdy.Api.Tests.Infrastructure;
using Nerdy.Contracts;

namespace Nerdy.Api.Tests;

public sealed class RoleSuggesterTests(NerdyApiFactory factory) : IClassFixture<NerdyApiFactory>
{
    [Theory]
    [InlineData("ana@stanford.edu", UserRole.Student, RoleSuggestionSource.School)]
    [InlineData("s1234567@student.unimelb.edu.au", UserRole.Student, RoleSuggestionSource.School)]
    [InlineData("ana@students.uni-heidelberg.de", UserRole.Student, RoleSuggestionSource.School)]
    [InlineData("li@mails.tsinghua.edu.cn", UserRole.Student, RoleSuggestionSource.School)]
    [InlineData("j.doe@staff.ox.ac.uk", UserRole.Teacher, RoleSuggestionSource.SchoolStaff)]
    [InlineData("prof.lee@kaist.ac.kr", UserRole.Teacher, RoleSuggestionSource.SchoolStaff)]
    [InlineData("teacher.kim@lausd.k12.ca.us", UserRole.Teacher, RoleSuggestionSource.SchoolStaff)]
    [InlineData("ana@alumni.stanford.edu", UserRole.Learner, RoleSuggestionSource.Alumni)]
    [InlineData("sam@gmail.com", UserRole.Learner, RoleSuggestionSource.Personal)]
    [InlineData("sam@Outlook.com", UserRole.Learner, RoleSuggestionSource.Personal)]
    [InlineData("sam@yahoo.co.uk", UserRole.Learner, RoleSuggestionSource.Personal)]
    [InlineData("sam@gmx.de", UserRole.Learner, RoleSuggestionSource.Personal)]
    [InlineData("r.chen@contoso.com", UserRole.Researcher, RoleSuggestionSource.Organization)]
    [InlineData("r.chen@labs.contoso.co.uk", UserRole.Researcher, RoleSuggestionSource.Organization)]
    public void Suggests_a_role_from_the_email_domain(string email, UserRole role, RoleSuggestionSource source)
    {
        var suggestion = RoleSuggester.Suggest(email);

        Assert.NotNull(suggestion);
        Assert.Equal(role, suggestion.Role);
        Assert.Equal(source, suggestion.Source);
        Assert.Equal(email[(email.IndexOf('@') + 1)..].ToLowerInvariant(), suggestion.Domain);
    }

    [Theory]
    [InlineData("sam.dev@nerdy.test")]
    [InlineData("user@example.com")]
    [InlineData("not-an-email")]
    [InlineData("")]
    public void Reserved_or_invalid_addresses_get_no_suggestion(string email) => Assert.Null(RoleSuggester.Suggest(email));

    [Fact]
    public async Task Me_includes_the_suggestion_until_onboarding_is_done()
    {
        var user = await factory.RegisterAsync(email: $"t{Guid.NewGuid():N}@staff.nerdy-university.edu");

        var before = await (await user.Client.GetAsync("/api/me")).ReadAsync<UserDto>();
        await user.Client.PatchAsJsonAsync("/api/me", new UpdateProfileRequest { Role = UserRole.Teacher, OnboardingCompleted = true });
        var after = await (await user.Client.GetAsync("/api/me")).ReadAsync<UserDto>();

        Assert.Equal(UserRole.Teacher, before.RoleSuggestion?.Role);
        Assert.Null(after.RoleSuggestion);
    }
}
