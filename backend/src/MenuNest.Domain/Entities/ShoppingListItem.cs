using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A single row on a <see cref="ShoppingList"/>, representing how much
/// of one ingredient still needs to be bought. Checking the item off
/// records who bought it and when.
/// </summary>
public sealed class ShoppingListItem : Entity
{
    public Guid ShoppingListId { get; private set; }
    public Guid IngredientId { get; private set; }
    public decimal Quantity { get; private set; }
    public bool IsBought { get; private set; }
    public DateTime? BoughtAt { get; private set; }
    public Guid? BoughtByUserId { get; private set; }

    private List<Guid> _sourceMealPlanEntryIds = new();
    public IReadOnlyList<Guid> SourceMealPlanEntryIds => _sourceMealPlanEntryIds.AsReadOnly();

    // EF Core
    private ShoppingListItem() { }

    internal static ShoppingListItem Create(
        Guid shoppingListId,
        Guid ingredientId,
        decimal quantity,
        IEnumerable<Guid>? sourceMealPlanEntryIds = null)
    {
        if (quantity <= 0m)
        {
            throw new DomainException("Shopping list quantity must be positive.");
        }

        return new ShoppingListItem
        {
            ShoppingListId = shoppingListId,
            IngredientId = ingredientId,
            Quantity = quantity,
            _sourceMealPlanEntryIds = sourceMealPlanEntryIds?.ToList() ?? new List<Guid>()
        };
    }

    internal void IncreaseQuantity(decimal delta, IEnumerable<Guid>? additionalSources = null)
    {
        if (delta <= 0m)
        {
            throw new DomainException("Quantity delta must be positive.");
        }

        Quantity += delta;
        if (additionalSources is not null)
        {
            foreach (var id in additionalSources)
            {
                if (!_sourceMealPlanEntryIds.Contains(id))
                {
                    _sourceMealPlanEntryIds.Add(id);
                }
            }
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateQuantity(decimal quantity)
    {
        if (quantity <= 0m)
        {
            throw new DomainException("Shopping list quantity must be positive.");
        }

        Quantity = quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkBought(Guid userId)
    {
        if (IsBought) return;

        IsBought = true;
        BoughtAt = DateTime.UtcNow;
        BoughtByUserId = userId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Unmark()
    {
        if (!IsBought) return;

        IsBought = false;
        BoughtAt = null;
        BoughtByUserId = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
