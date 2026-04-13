using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A shopping list belonging to a family. Items are aggregated by
/// ingredient so adding the same ingredient twice sums the quantity
/// rather than creating duplicate rows.
/// </summary>
public sealed class ShoppingList : Entity
{
    public Guid FamilyId { get; private set; }
    public string Name { get; private set; } = null!;
    public ShoppingListStatus Status { get; private set; } = ShoppingListStatus.Active;
    public Guid CreatedByUserId { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    private readonly List<ShoppingListItem> _items = new();
    public IReadOnlyCollection<ShoppingListItem> Items => _items.AsReadOnly();

    // EF Core
    private ShoppingList() { }

    public static ShoppingList Create(Guid familyId, string name, Guid createdByUserId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Shopping list name is required.");
        }

        return new ShoppingList
        {
            FamilyId = familyId,
            Name = name.Trim(),
            CreatedByUserId = createdByUserId
        };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Shopping list name is required.");
        }

        Name = name.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public ShoppingListItem AddOrIncreaseItem(
        Guid ingredientId,
        decimal quantity,
        IEnumerable<Guid>? sourceMealPlanEntryIds = null)
    {
        if (Status != ShoppingListStatus.Active)
        {
            throw new DomainException("Cannot modify a list that is not active.");
        }

        var existing = _items.FirstOrDefault(i => i.IngredientId == ingredientId);
        if (existing is not null)
        {
            existing.IncreaseQuantity(quantity, sourceMealPlanEntryIds);
            return existing;
        }

        var item = ShoppingListItem.Create(Id, ingredientId, quantity, sourceMealPlanEntryIds);
        _items.Add(item);
        UpdatedAt = DateTime.UtcNow;
        return item;
    }

    public void RemoveItem(Guid itemId)
    {
        if (Status != ShoppingListStatus.Active)
        {
            throw new DomainException("Cannot modify a list that is not active.");
        }

        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return;

        _items.Remove(item);
        UpdatedAt = DateTime.UtcNow;
    }

    public void Complete()
    {
        Status = ShoppingListStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Archive()
    {
        Status = ShoppingListStatus.Archived;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reactivate()
    {
        if (Status == ShoppingListStatus.Active) return;

        Status = ShoppingListStatus.Active;
        CompletedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
