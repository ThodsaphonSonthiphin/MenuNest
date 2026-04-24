using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A named grouping of <see cref="BudgetCategory"/> items within a family's
/// budget (e.g. "Bills", "Fun"). Groups order categories in the envelope view
/// and can be hidden without deleting.
/// </summary>
public sealed class BudgetCategoryGroup : Entity
{
    public Guid FamilyId { get; private set; }
    public string Name { get; private set; } = null!;
    public int SortOrder { get; private set; }
    public bool IsHidden { get; private set; }

    private BudgetCategoryGroup() { }

    public static BudgetCategoryGroup Create(Guid familyId, string name, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Group name is required.");
        return new BudgetCategoryGroup
        {
            FamilyId = familyId,
            Name = name.Trim(),
            SortOrder = sortOrder,
            IsHidden = false
        };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Group name is required.");
        Name = name.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetSortOrder(int s) { SortOrder = s; UpdatedAt = DateTime.UtcNow; }
    public void Hide()   { IsHidden = true;  UpdatedAt = DateTime.UtcNow; }
    public void Unhide() { IsHidden = false; UpdatedAt = DateTime.UtcNow; }
}
