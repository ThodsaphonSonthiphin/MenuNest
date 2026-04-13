using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("StockItems");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.FamilyId).IsRequired();
        builder.Property(s => s.IngredientId).IsRequired();

        builder.Property(s => s.Quantity)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(s => s.UpdatedByUserId).IsRequired();

        builder.HasIndex(s => new { s.FamilyId, s.IngredientId }).IsUnique();

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(s => s.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Ingredient>()
            .WithMany()
            .HasForeignKey(s => s.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
