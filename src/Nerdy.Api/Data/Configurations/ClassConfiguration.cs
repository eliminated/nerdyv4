using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerdy.Api.Data.Entities;
using Nerdy.Api.Features.Classes;

namespace Nerdy.Api.Data.Configurations;

// Cascade paths: deleting a user cascades to the classes they own, and from there to memberships, assignments and
// completions. The direct user -> membership and user -> completion keys are NO ACTION because SQL Server rejects
// a second cascade path to the same table; DELETE /api/me removes those rows explicitly first.

internal sealed class ClassroomConfiguration : IEntityTypeConfiguration<Classroom>
{
    public void Configure(EntityTypeBuilder<Classroom> classroom)
    {
        classroom.HasKey(c => c.Id);
        classroom.Property(c => c.Name).HasMaxLength(FieldLimits.Name);
        classroom.Property(c => c.Subject).HasMaxLength(FieldLimits.Name);
        classroom.Property(c => c.ColorHex).HasMaxLength(FieldLimits.ColorHex);
        classroom.Property(c => c.JoinCode).HasMaxLength(JoinCodes.Length);
        classroom.HasIndex(c => c.JoinCode).IsUnique();
        classroom.HasOne(c => c.Owner).WithMany().HasForeignKey(c => c.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ClassMembershipConfiguration : IEntityTypeConfiguration<ClassMembership>
{
    public void Configure(EntityTypeBuilder<ClassMembership> membership)
    {
        membership.HasKey(m => new { m.ClassroomId, m.UserId });
        membership.HasIndex(m => m.UserId);
        membership.HasOne(m => m.Classroom).WithMany(c => c.Members).HasForeignKey(m => m.ClassroomId).OnDelete(DeleteBehavior.Cascade);
        membership.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> assignment)
    {
        assignment.HasKey(a => a.Id);
        assignment.Property(a => a.Title).HasMaxLength(FieldLimits.Name);
        assignment.HasIndex(a => new { a.ClassroomId, a.DueAt });
        assignment.HasOne(a => a.Classroom).WithMany(c => c.Assignments).HasForeignKey(a => a.ClassroomId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AssignmentCompletionConfiguration : IEntityTypeConfiguration<AssignmentCompletion>
{
    public void Configure(EntityTypeBuilder<AssignmentCompletion> completion)
    {
        completion.HasKey(c => new { c.AssignmentId, c.UserId });
        completion.HasIndex(c => c.UserId);
        completion.HasOne(c => c.Assignment).WithMany(a => a.Completions).HasForeignKey(c => c.AssignmentId).OnDelete(DeleteBehavior.Cascade);
        completion.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
