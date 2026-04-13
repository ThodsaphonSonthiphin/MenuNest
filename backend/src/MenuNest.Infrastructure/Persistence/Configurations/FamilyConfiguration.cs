using MenuNest.Domain.Entities;
using MenuNest.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class FamilyConfiguration : IEntityTypeConfiguration<Family>
{
    public void Configure(EntityTypeBuilder<Family> builder)
    {
        builder.ToTable("Families");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.Name)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(f => f.InviteCode)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion(
                ic => ic.Value,
                raw => InviteCode.From(raw));

        builder.HasIndex("InviteCode").IsUnique();

        builder.Property(f => f.CreatedAt).IsRequired();
        builder.Property(f => f.UpdatedAt);
        builder.Property(f => f.CreatedByUserId).IsRequired();

        builder.HasMany(f => f.Members)
            .WithOne(u => u.Family!)
            .HasForeignKey(u => u.FamilyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
