using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerdy.Api.Data.Entities;

namespace Nerdy.Api.Data.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> user)
    {
        user.Property(u => u.DisplayName).HasMaxLength(FieldLimits.DisplayName);

        // Identity checks RequireUniqueEmail in code; the unique index closes the race between two registrations.
        user.HasIndex(u => u.NormalizedEmail).IsUnique();
    }
}
