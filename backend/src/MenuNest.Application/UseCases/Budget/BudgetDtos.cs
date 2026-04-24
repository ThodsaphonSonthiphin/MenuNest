using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Budget;

// ---------- Accounts ----------
public sealed record BudgetAccountDto(
    Guid Id, string Name, BudgetAccountType Type, decimal Balance, int SortOrder, bool IsClosed);

public sealed record CreateAccountRequest(
    string Name, BudgetAccountType Type, decimal OpeningBalance, int SortOrder);

public sealed record UpdateAccountRequest(
    string Name, int SortOrder, bool IsClosed, decimal? SetBalance);

// ---------- Groups ----------
public sealed record CategoryGroupDto(Guid Id, string Name, int SortOrder, bool IsHidden);

public sealed record UpsertGroupRequest(string Name, int SortOrder);

// ---------- Categories ----------
public sealed record BudgetCategoryDto(
    Guid Id, Guid GroupId, string Name, string? Emoji, int SortOrder, bool IsHidden,
    BudgetTargetType TargetType, decimal? TargetAmount,
    DateOnly? TargetDueDate, int? TargetDayOfMonth);

public sealed record UpsertCategoryRequest(
    Guid GroupId, string Name, string? Emoji, int SortOrder,
    BudgetTargetType TargetType, decimal? TargetAmount,
    DateOnly? TargetDueDate, int? TargetDayOfMonth);

// ---------- Monthly summary ----------
public sealed record EnvelopeDto(
    Guid CategoryId, string Name, string? Emoji, int SortOrder, bool IsHidden,
    decimal Assigned,
    decimal Activity,
    decimal Available,
    BudgetTargetType TargetType,
    decimal? TargetAmount,
    DateOnly? TargetDueDate,
    int? TargetDayOfMonth,
    decimal? TargetProgressFraction,    // 0..1, null if no target
    string? TargetHint);                 // e.g. "฿300.00 more needed by the 1st"

public sealed record EnvelopeGroupDto(
    Guid GroupId, string Name, int SortOrder, bool IsHidden,
    decimal TotalAssigned, decimal TotalActivity, decimal TotalAvailable,
    IReadOnlyList<EnvelopeDto> Categories);

public sealed record MonthlySummaryDto(
    int Year, int Month,
    decimal Income,
    decimal TotalAssigned,
    decimal TotalActivity,
    decimal ReadyToAssign,              // income + leftover carry-in − totalAssigned
    decimal LeftOverFromLastMonth,
    decimal Available,                  // leftOver + assigned + activity (money still in envelopes)
    IReadOnlyList<EnvelopeGroupDto> Groups,
    IReadOnlyList<BudgetAccountDto> Accounts);

// ---------- Transactions ----------
public sealed record BudgetTransactionDto(
    Guid Id, Guid AccountId, string AccountName,
    Guid? CategoryId, string? CategoryName, string? CategoryEmoji,
    decimal Amount, DateOnly Date, string? Notes,
    Guid CreatedByUserId, string CreatedByDisplayName);

public sealed record CreateTransactionRequest(
    Guid AccountId, Guid? CategoryId, decimal Amount, DateOnly Date, string? Notes);

public sealed record UpdateTransactionRequest(
    Guid AccountId, Guid? CategoryId, decimal Amount, DateOnly Date, string? Notes);

// ---------- Monthly ops ----------
public sealed record SetAssignedRequest(Guid CategoryId, int Year, int Month, decimal Amount);
public sealed record SetMonthlyIncomeRequest(int Year, int Month, decimal Amount);
public sealed record MoveMoneyRequest(
    Guid FromCategoryId, Guid ToCategoryId, int Year, int Month, decimal Amount);
public sealed record CoverOverspendingRequest(
    Guid OverspentCategoryId, Guid FromCategoryId, int Year, int Month, decimal Amount);
