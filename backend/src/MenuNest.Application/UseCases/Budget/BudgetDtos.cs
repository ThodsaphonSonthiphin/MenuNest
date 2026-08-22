using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Budget;

// ---------- Accounts ----------
public sealed record BudgetAccountDto(
    Guid Id, string Name, BudgetAccountType Type, decimal Balance, int SortOrder, bool IsClosed);

public sealed record CreateAccountRequest(
    string Name, BudgetAccountType Type, decimal OpeningBalance, int SortOrder);

public sealed record UpdateAccountRequest(
    string Name, int SortOrder, bool IsClosed);

// menunest-182: replaces the deleted BudgetAccount.SetBalance. TimeZoneId
// (menunest-189) is the viewer's IANA zone — required on every call, since
// the derived balance and the default correction date both read "today".
public sealed record CorrectBalanceRequest(
    decimal ActualBalance, bool Confirmed, DateOnly? Date, string? Notes, string? TimeZoneId);

public sealed record BalanceCorrectionResultDto(
    bool Written, decimal DerivedBalance, decimal Difference, string Message);

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
    string? TargetHint,                  // e.g. "฿300.00 more needed by the 1st"
    bool IsEveryday);                    // menunest-181/184 — feeds the Daily allowance; set in bulk from EverydayMarksSheet

public sealed record EnvelopeGroupDto(
    Guid GroupId, string Name, int SortOrder, bool IsHidden,
    decimal TotalAssigned, decimal TotalActivity, decimal TotalAvailable,
    IReadOnlyList<EnvelopeDto> Categories);

public sealed record MonthlySummaryDto(
    int Year, int Month,
    decimal Income,
    decimal TotalAssigned,
    decimal TotalActivity,
    decimal ReadyToAssign,              // sum(accounts) − sum(envelope.available)
    decimal Available,                  // sum of envelope Available amounts
    IReadOnlyList<EnvelopeGroupDto> Groups,
    IReadOnlyList<BudgetAccountDto> Accounts,
    DailyAllowanceDto? DailyAllowance = null); // null unless Year/Month is the real current month (menunest-185)

// ---------- Daily allowance (menunest-181) ----------
/// <summary>
/// The frozen "you can spend this much today" card. <c>PaceDelta</c> is
/// actual-minus-should: positive is over pace, negative is under, zero
/// renders nothing. <c>HasMarks</c> false collapses both "nothing marked"
/// and "no frozen row yet" into one empty state — there is no third state.
/// </summary>
public sealed record DailyAllowanceDto(decimal Amount, DateOnly FrozenOn, decimal PaceDelta, bool HasMarks);

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
// TimeZoneId (menunest-189) is the viewer's IANA zone, e.g. from the SPA's
// Intl.DateTimeFormat().resolvedOptions().timeZone. Only actually resolved
// when the op touches an everyday envelope and re-freezes the Daily allowance.
public sealed record SetAssignedRequest(Guid CategoryId, int Year, int Month, decimal Amount, string? TimeZoneId);
public sealed record MoveMoneyRequest(
    Guid FromCategoryId, Guid ToCategoryId, int Year, int Month, decimal Amount, string? TimeZoneId);
public sealed record CoverOverspendingRequest(
    Guid OverspentCategoryId, Guid FromCategoryId, int Year, int Month, decimal Amount, string? TimeZoneId);

// ---------- Account detail (transactions feed) ----------
public sealed record AccountSummaryDto(
    Guid Id,
    string Name,
    BudgetAccountType Type,
    decimal Balance,
    decimal MonthInflow,    // sum of positive amounts where Date in given Year/Month
    decimal MonthOutflow    // sum of negative amounts where Date in given Year/Month (stored negative)
);

public sealed record AccountTransactionsPageDto(
    AccountSummaryDto Account,
    IReadOnlyList<BudgetTransactionDto> Items,
    bool HasMore
);
