using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class IngredientConfiguration : IEntityTypeConfiguration<Ingredient>
{
    public void Configure(EntityTypeBuilder<Ingredient> builder)
    {
        builder.ToTable("Ingredients");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.FamilyId).IsRequired();

        builder.Property(i => i.Name)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(i => i.Unit)
            .IsRequired()
            .HasMaxLength(40);

        builder.HasIndex(i => new { i.FamilyId, i.Name }).IsUnique();

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(i => i.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
