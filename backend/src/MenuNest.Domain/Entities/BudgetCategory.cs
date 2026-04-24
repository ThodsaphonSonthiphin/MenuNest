using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// An envelope within a <see cref="BudgetCategoryGroup"/>. Carries its own
/// target/goal configuration; activity and available are computed from
/// transactions and monthly assignments rather than stored here.
/// </summary>
public sealed class BudgetCategory : Entity
{
    public Guid FamilyId { get; private set; }
    public Guid GroupId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Emoji { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsHidden { get; private set; }

    // Target / goal
    public BudgetTargetType TargetType { get; private set; }
    public decimal? TargetAmount { get; private set; }
    public DateOnly? TargetDueDate { get; private set; }
    public int? TargetDayOfMonth { get; private set; }

    private BudgetCategory() { }

    public static BudgetCategory Create(
        Guid familyId, Guid groupId, string name, string? emoji, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Category name is required.");
        return new BudgetCategory
        {
            FamilyId = familyId,
            GroupId = groupId,
            Name = name.Trim(),
            Emoji = string.IsNullOrWhiteSpace(emoji) ? null : emoji.Trim(),
            SortOrder = sortOrder,
            IsHidden = false,
            TargetType = BudgetTargetType.None
        };
    }

    public void Update(string name, string? emoji, Guid groupId, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Category name is required.");
        Name = name.Trim();
        Emoji = string.IsNullOrWhiteSpace(emoji) ? null : emoji.Trim();
        GroupId = groupId;
        SortOrder = sortOrder;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ClearTarget()
    {
        TargetType = BudgetTargetType.None;
        TargetAmount = null;
        TargetDueDate = null;
        TargetDayOfMonth = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetMonthlyTarget(decimal amount, int? dayOfMonth)
    {
        if (amount <= 0) throw new DomainException("Target amount must be positive.");
        TargetType = BudgetTargetType.MonthlyAmount;
        TargetAmount = amount;
        TargetDayOfMonth = dayOfMonth;
        TargetDueDate = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetByDateTarget(decimal amount, DateOnly dueDate)
    {
        if (amount <= 0) throw new DomainException("Target amount must be positive.");
        TargetType = BudgetTargetType.ByDate;
        TargetAmount = amount;
        TargetDueDate = dueDate;
        TargetDayOfMonth = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Hide()   { IsHidden = true;  UpdatedAt = DateTime.UtcNow; }
    public void Unhide() { IsHidden = false; UpdatedAt = DateTime.UtcNow; }
}
