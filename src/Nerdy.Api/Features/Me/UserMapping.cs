using Microsoft.AspNetCore.Identity;
using Nerdy.Api.Data.Entities;
using Nerdy.Contracts;

namespace Nerdy.Api.Features.Me;

public static class UserMapping
{
    public static async Task<UserDto> ToDtoAsync(this UserManager<AppUser> users, AppUser user)
    {
        var logins = await users.GetLoginsAsync(user);
        return new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? "",
            DisplayName = user.DisplayName,
            Role = user.Role,
            OnboardingCompleted = user.OnboardingCompleted,
            EmailConfirmed = user.EmailConfirmed,
            HasPassword = user.PasswordHash is not null,
            ExternalProviders = logins.Select(l => l.LoginProvider).Distinct().Order().ToList(),
            CreatedAt = user.CreatedAt,
            RoleSuggestion = user.OnboardingCompleted ? null : RoleSuggester.Suggest(user.Email),
        };
    }
}
