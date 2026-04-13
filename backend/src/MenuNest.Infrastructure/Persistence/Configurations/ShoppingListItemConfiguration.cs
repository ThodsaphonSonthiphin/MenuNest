using System.Text.Json;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class ShoppingListItemConfiguration : IEntityTypeConfiguration<ShoppingListItem>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public void Configure(EntityTypeBuilder<ShoppingListItem> builder)
    {
        builder.ToTable("ShoppingListItems");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.ShoppingListId).IsRequired();
        builder.Property(i => i.IngredientId).IsRequired();

        builder.Property(i => i.Quantity)
            .IsRequired()
            .HasPrecision(18, 4);

        builder.Property(i => i.IsBought).IsRequired();
        builder.Property(i => i.BoughtAt);
        builder.Property(i => i.BoughtByUserId);

        // Serialise the source meal-plan list as a JSON column so we
        // never need a join table for what is really an informational
        // tag on the row.
        var sourceIdsComparer = new ValueComparer<IReadOnlyList<Guid>>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            v => v.Aggregate(0, (hash, id) => HashCode.Combine(hash, id.GetHashCode())),
            v => v.ToList());

        builder.Property(i => i.SourceMealPlanEntryIds)
            .HasColumnName("SourceMealPlanEntryIdsJson")
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<Guid>>(v, JsonOptions) ?? new List<Guid>(),
                sourceIdsComparer);

        builder.HasIndex(i => new { i.ShoppingListId, i.IngredientId }).IsUnique();

        builder.HasOne<Ingredient>()
            .WithMany()
            .HasForeignKey(i => i.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
