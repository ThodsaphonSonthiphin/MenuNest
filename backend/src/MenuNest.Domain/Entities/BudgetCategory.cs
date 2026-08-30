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

    /// <summary>
    /// Marks this envelope as day-to-day spending — the only kind that feeds the
    /// Daily allowance (menunest-181). Lives on the envelope, never on its group,
    /// so it survives a move between groups.
    /// </summary>
    public bool IsEveryday { get; private set; }

    /// <summary>
    /// Non-null exactly on a Payment envelope — the Credit account this envelope
    /// holds money to pay (menunest-202). One per account, enforced by a filtered
    /// unique index. A Loan account never has one (menunest-206).
    /// </summary>
    public Guid? PaymentForAccountId { get; private set; }

    public bool IsPaymentEnvelope => PaymentForAccountId.HasValue;

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
            IsEveryday = false,
            TargetType = BudgetTargetType.None
        };
    }

    public static BudgetCategory CreatePaymentEnvelope(
        Guid familyId, Guid groupId, Guid accountId, string accountName, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            throw new DomainException("Account name is required.");
        return new BudgetCategory
        {
            FamilyId = familyId,
            GroupId = groupId,
            Name = $"จ่ายบัตร {accountName.Trim()}",
            Emoji = "💳",
            SortOrder = sortOrder,
            IsHidden = false,
            IsEveryday = false,
            TargetType = BudgetTargetType.None,
            PaymentForAccountId = accountId
        };
    }

    /// <summary>
    /// The only path that may retitle a Payment envelope: its name follows its
    /// Account (menunest-205, menunest-212), so an account rename pushes through
    /// here while <see cref="Update"/> stays closed.
    /// </summary>
    public void RenameForAccount(string accountName)
    {
        if (!IsPaymentEnvelope)
            throw new DomainException("Not a payment envelope.");
        if (string.IsNullOrWhiteSpace(accountName))
            throw new DomainException("Account name is required.");
        Name = $"จ่ายบัตร {accountName.Trim()}";
        UpdatedAt = DateTime.UtcNow;
    }

    public void Update(string name, string? emoji, Guid groupId, int sortOrder)
    {
        if (IsPaymentEnvelope)
            throw new DomainException(
                "A payment envelope cannot be renamed or moved — its name follows its account.");
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

    public void SetMonthlySavingsBuilderTarget(decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("Target amount must be positive.");
        }

        TargetType = BudgetTargetType.MonthlySavingsBuilder;
        TargetAmount = amount;
        TargetDayOfMonth = null;
        TargetDueDate = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Hide()
    {
        if (IsPaymentEnvelope)
            throw new DomainException("A payment envelope cannot be hidden.");
        IsHidden = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Unhide() { IsHidden = false; UpdatedAt = DateTime.UtcNow; }

    /// <summary>
    /// Closing a Credit account hides its Payment envelope (menunest-210). That is
    /// the app's own act, not the User's, so it bypasses <see cref="Hide"/>'s guard.
    /// </summary>
    public void SetHiddenForAccountClosure(bool hidden)
    {
        if (!IsPaymentEnvelope) throw new DomainException("Not a payment envelope.");
        IsHidden = hidden;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkEveryday(bool isEveryday)
    {
        if (isEveryday && IsPaymentEnvelope)
            throw new DomainException(
                "A payment envelope cannot be an everyday envelope — it would inflate the daily allowance.");
        IsEveryday = isEveryday;
        UpdatedAt = DateTime.UtcNow;
    }
}
