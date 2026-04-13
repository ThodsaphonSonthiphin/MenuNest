using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class RecipeIngredientConfiguration : IEntityTypeConfiguration<RecipeIngredient>
{
    public void Configure(EntityTypeBuilder<RecipeIngredient> builder)
    {
        builder.ToTable("RecipeIngredients");

        builder.HasKey(ri => ri.Id);
        builder.Property(ri => ri.Id).ValueGeneratedNever();

        builder.Property(ri => ri.RecipeId).IsRequired();
        builder.Property(ri => ri.IngredientId).IsRequired();

        builder.Property(ri => ri.Quantity)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.HasIndex(ri => new { ri.RecipeId, ri.IngredientId }).IsUnique();

        builder.HasOne<Ingredient>()
            .WithMany()
            .HasForeignKey(ri => ri.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
