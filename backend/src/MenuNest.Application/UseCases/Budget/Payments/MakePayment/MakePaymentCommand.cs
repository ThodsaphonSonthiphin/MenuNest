using Mediator;

namespace MenuNest.Application.UseCases.Budget.Payments.MakePayment;

/// <summary>
/// menunest-204 / menunest-207: pays down a Credit or Loan account. Writes BOTH
/// legs in one unit of work — there is no moment at which half a payment exists.
/// Date defaults to the viewer's local today (menunest-189).
/// </summary>
public sealed record MakePaymentCommand(
    Guid FromAccountId, Guid ToAccountId, decimal Amount,
    DateOnly? Date, string? Notes, string? TimeZoneId) : ICommand<PaymentDto>;
