using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class StockTransactionConfiguration : IEntityTypeConfiguration<StockTransaction>
{
    public void Configure(EntityTypeBuilder<StockTransaction> builder)
    {
        builder.ToTable("StockTransactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.FamilyId).IsRequired();
        builder.Property(t => t.IngredientId).IsRequired();

        builder.Property(t => t.Delta)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(t => t.Source).IsRequired();
        builder.Property(t => t.SourceRefId);
        builder.Property(t => t.CreatedByUserId).IsRequired();

        builder.Property(t => t.Notes)
            .HasMaxLength(500);

        builder.HasIndex(t => new { t.FamilyId, t.IngredientId, t.CreatedAt });

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(t => t.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Ingredient>()
            .WithMany()
            .HasForeignKey(t => t.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
