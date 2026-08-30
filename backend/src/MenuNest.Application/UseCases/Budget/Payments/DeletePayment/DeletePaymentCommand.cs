using Mediator;

namespace MenuNest.Application.UseCases.Budget.Payments.DeletePayment;

/// <summary>
/// menunest-209: a payment is one row to the user — deletes BOTH legs
/// sharing <paramref name="PaymentId"/> in one <c>SaveChangesAsync</c>.
/// Deleting only one leg would leave the debt paid in the budget and unpaid
/// on the card (or vice versa) with no soft-delete to recover from.
/// </summary>
public sealed record DeletePaymentCommand(Guid PaymentId) : ICommand<Unit>;
