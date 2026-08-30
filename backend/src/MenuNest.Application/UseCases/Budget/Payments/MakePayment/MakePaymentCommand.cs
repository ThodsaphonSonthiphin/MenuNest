using Mediator;

namespace MenuNest.Application.UseCases.Budget.Payments.MakePayment;

/// <summary>
/// menunest-204 / menunest-207 / menunest-214: pays down a Credit or Loan account.
/// Writes BOTH legs in one unit of work — there is no moment at which half a
/// payment exists. Date defaults to the viewer's local today (menunest-189).
///
/// <paramref name="CategoryId"/> (menunest-214) is the Envelope the money for the
/// instalment comes from — REQUIRED when <c>ToAccountId</c> is a Loan (it has no
/// Payment envelope of its own, menunest-206, so this is the only Envelope a loan
/// payment ever spends) and REFUSED when it is a Credit account (its Payment
/// envelope already falls by derivation; categorising the from-leg too would
/// double-spend one payment across two envelopes). Applied to the from-leg only —
/// the leg landing on the debt account is always uncategorised.
/// </summary>
public sealed record MakePaymentCommand(
    Guid FromAccountId, Guid ToAccountId, decimal Amount,
    DateOnly? Date, string? Notes, string? TimeZoneId, Guid? CategoryId = null) : ICommand<PaymentDto>;
