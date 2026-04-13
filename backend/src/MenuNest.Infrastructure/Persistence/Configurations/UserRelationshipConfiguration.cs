using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class UserRelationshipConfiguration : IEntityTypeConfiguration<UserRelationship>
{
    public void Configure(EntityTypeBuilder<UserRelationship> builder)
    {
        builder.ToTable("UserRelationships");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.FamilyId).IsRequired();
        builder.Property(r => r.FromUserId).IsRequired();
        builder.Property(r => r.ToUserId).IsRequired();
        builder.Property(r => r.RelationType).IsRequired();

        builder.HasIndex(r => new { r.FamilyId, r.FromUserId, r.ToUserId, r.RelationType }).IsUnique();

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(r => r.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.FromUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.ToUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
