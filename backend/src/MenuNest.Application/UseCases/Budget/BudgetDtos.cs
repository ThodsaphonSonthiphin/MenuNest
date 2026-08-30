using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Budget;

// ---------- Accounts ----------
public sealed record BudgetAccountDto(
    Guid Id, string Name, BudgetAccountType Type, decimal Balance, int SortOrder, bool IsClosed,
    // menunest-202: what is still owed and not yet funded. NULL on anything but a
    // Credit account — a Loan has no Payment envelope (menunest-206), so it must
    // never read "ขาดอีก" for its whole outstanding balance.
    decimal? Shortfall = null);

// TimeZoneId (menunest-189) is the viewer's IANA zone — only actually
// resolved when OpeningBalance is non-zero and an opening-balance
// transaction needs to be dated.
public sealed record CreateAccountRequest(
    string Name, BudgetAccountType Type, decimal OpeningBalance, int SortOrder, string? TimeZoneId);

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
    bool IsEveryday,                     // menunest-181/184 — feeds the Daily allowance; set in bulk from EverydayMarksSheet
    Guid? PaymentForAccountId = null,   // non-null ⇒ this is a Payment envelope
    decimal? Shortfall = null,          // §4.3, non-null only on a Payment envelope
    // R-1: −Σ(categorised rows on the card) for the selected month. Positive
    // when the card was used. Non-null only on a Payment envelope — for those,
    // Assigned + Activity alone does not explain the change in Available (a
    // categorised card purchase moves Available while both stay 0), so this is
    // the display term the UI shows instead of Activity. Month-scoped, like
    // Assigned and Activity: Available == Assigned + CardSpending + Activity.
    decimal? CardSpending = null);

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
/// <summary>
/// <paramref name="BatchId"/> (menunest-196) groups the N assigns one press of a
/// quick-assign chip makes into a SINGLE history row. The SPA generates one id
/// per press and sends it on every call in that press; a lone assign sends null.
/// </summary>
public sealed record SetAssignedRequest(
    Guid CategoryId, int Year, int Month, decimal Amount, string? TimeZoneId, Guid? BatchId);
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
