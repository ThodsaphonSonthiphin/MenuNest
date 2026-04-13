using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// The running quantity of an ingredient the family currently has on
/// hand. Exactly one row exists per (FamilyId, IngredientId).
/// </summary>
public sealed class StockItem : Entity
{
    public Guid FamilyId { get; private set; }
    public Guid IngredientId { get; private set; }
    public decimal Quantity { get; private set; }
    public Guid UpdatedByUserId { get; private set; }

    // EF Core
    private StockItem() { }

    public static StockItem Create(
        Guid familyId,
        Guid ingredientId,
        decimal initialQuantity,
        Guid updatedByUserId)
    {
        if (initialQuantity < 0m)
        {
            throw new DomainException("Stock quantity cannot be negative.");
        }

        return new StockItem
        {
            FamilyId = familyId,
            IngredientId = ingredientId,
            Quantity = initialQuantity,
            UpdatedByUserId = updatedByUserId
        };
    }

    public void SetQuantity(decimal quantity, Guid userId)
    {
        if (quantity < 0m)
        {
            throw new DomainException("Stock quantity cannot be negative.");
        }

        Quantity = quantity;
        UpdatedByUserId = userId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Applies a delta, clamping the resulting quantity at zero. Returns
    /// the delta that was actually applied — if a cook action requests
    /// -2 but only 1 is in stock, returns -1 (and the caller can log
    /// the shortfall to CookNotes / shopping list).
    /// </summary>
    public decimal ApplyDelta(decimal delta, Guid userId)
    {
        var newQty = Quantity + delta;
        var applied = delta;

        if (newQty < 0m)
        {
            applied = -Quantity;
            newQty = 0m;
        }

        Quantity = newQty;
        UpdatedByUserId = userId;
        UpdatedAt = DateTime.UtcNow;
        return applied;
    }
}
