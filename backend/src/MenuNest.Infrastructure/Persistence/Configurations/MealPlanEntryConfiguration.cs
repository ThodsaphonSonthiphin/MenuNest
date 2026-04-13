using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class MealPlanEntryConfiguration : IEntityTypeConfiguration<MealPlanEntry>
{
    public void Configure(EntityTypeBuilder<MealPlanEntry> builder)
    {
        builder.ToTable("MealPlanEntries");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.FamilyId).IsRequired();
        builder.Property(m => m.Date).IsRequired();
        builder.Property(m => m.MealSlot).IsRequired();
        builder.Property(m => m.RecipeId).IsRequired();
        builder.Property(m => m.CreatedByUserId).IsRequired();
        builder.Property(m => m.Status).IsRequired();

        builder.Property(m => m.Notes).HasMaxLength(500);
        builder.Property(m => m.CookNotes).HasMaxLength(500);

        builder.HasIndex(m => new { m.FamilyId, m.Date, m.MealSlot }).IsUnique();

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(m => m.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Recipe>()
            .WithMany()
            .HasForeignKey(m => m.RecipeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
