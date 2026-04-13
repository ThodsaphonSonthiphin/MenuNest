using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A line item inside a <see cref="Recipe"/>. Instances are only
/// created through <see cref="Recipe.AddIngredient"/> so the recipe
/// aggregate stays consistent.
/// </summary>
public sealed class RecipeIngredient : Entity
{
    public Guid RecipeId { get; private set; }
    public Guid IngredientId { get; private set; }
    public decimal Quantity { get; private set; }

    // EF Core
    private RecipeIngredient() { }

    internal static RecipeIngredient Create(Guid recipeId, Guid ingredientId, decimal quantity)
    {
        if (quantity <= 0m)
        {
            throw new DomainException("Ingredient quantity must be positive.");
        }

        return new RecipeIngredient
        {
            RecipeId = recipeId,
            IngredientId = ingredientId,
            Quantity = quantity
        };
    }

    internal void UpdateQuantity(decimal quantity)
    {
        if (quantity <= 0m)
        {
            throw new DomainException("Ingredient quantity must be positive.");
        }

        Quantity = quantity;
        UpdatedAt = DateTime.UtcNow;
    }
}
