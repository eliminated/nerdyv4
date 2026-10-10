using Microsoft.AspNetCore.Identity;
using Nerdy.Contracts;

namespace Nerdy.Api.Data.Entities;

/// <summary>An account. Identity owns credentials, lockout and external logins; we add the profile.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Student;
    public bool OnboardingCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
