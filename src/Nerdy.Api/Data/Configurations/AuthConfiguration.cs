using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerdy.Api.Data.Entities;

namespace Nerdy.Api.Data.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> token)
    {
        token.HasKey(t => t.Id);
        token.Property(t => t.TokenHash).HasMaxLength(FieldLimits.Hash);
        token.Property(t => t.ReplacedByHash).HasMaxLength(FieldLimits.Hash);
        token.HasIndex(t => t.TokenHash).IsUnique();
        token.HasIndex(t => t.FamilyId);
        token.HasIndex(t => t.UserId);
        token.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ExternalLoginCodeConfiguration : IEntityTypeConfiguration<ExternalLoginCode>
{
    public void Configure(EntityTypeBuilder<ExternalLoginCode> code)
    {
        code.HasKey(c => c.CodeHash);
        code.Property(c => c.CodeHash).HasMaxLength(FieldLimits.Hash);
        code.Property(c => c.CodeChallenge).HasMaxLength(FieldLimits.Hash);
        code.Property(c => c.RedirectUri).HasMaxLength(FieldLimits.RedirectUri);
        code.HasIndex(c => c.UserId);
        code.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
