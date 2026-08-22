using Mediator;

namespace MenuNest.Application.UseCases.Budget.Accounts.CorrectBalance;

/// <summary>
/// Replaces the deleted <c>BudgetAccount.SetBalance</c> (menunest-182): an
/// account's balance can only move by writing a transaction, never by a
/// silent overwrite. The user's explicit requirement was "ask me before you
/// change my balance" — enforced server-side, following ADR-140's
/// <c>Shrink</c> refuse-then-confirm precedent on <c>update_trip</c> (the
/// repo has no MCP tool annotations to lean on instead). The first call MUST
/// be sent with <see cref="Confirmed"/> = false; the handler refuses it and
/// returns the numbers the caller must show the user before resending with
/// <see cref="Confirmed"/> = true. The refusal text IS the question the user
/// gets asked — a description merely asking the model to confirm is a
/// request, not a gate.
/// <para>
/// <see cref="TimeZoneId"/> (menunest-189) is the viewer's IANA zone —
/// required on every call, not just when an everyday envelope is touched,
/// because the derived balance and the default correction date both read
/// the viewer's local "today" (see
/// <see cref="MenuNest.Application.UseCases.Budget.Allowance.BudgetTimeZone"/>).
/// </para>
/// </summary>
public sealed record CorrectBalanceCommand(
    Guid AccountId, decimal ActualBalance, bool Confirmed, DateOnly? Date, string? Notes, string? TimeZoneId)
    : ICommand<BalanceCorrectionResultDto>;
