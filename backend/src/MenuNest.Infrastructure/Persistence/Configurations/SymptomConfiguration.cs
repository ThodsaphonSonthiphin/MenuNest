using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class SymptomConfiguration : IEntityTypeConfiguration<Symptom>
{
    public void Configure(EntityTypeBuilder<Symptom> builder)
    {
        builder.ToTable("Symptoms");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Name).IsRequired().HasMaxLength(100);
        builder.Property(s => s.IsSeed).IsRequired();
        builder.Property(s => s.UserId);

        // Seed rows: UserId is null and rows are shared across all users.
        // Custom rows: UserId is the owning user.
        builder.HasIndex(s => new { s.UserId, s.Name }).IsUnique()
            .HasFilter("[UserId] IS NOT NULL");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
