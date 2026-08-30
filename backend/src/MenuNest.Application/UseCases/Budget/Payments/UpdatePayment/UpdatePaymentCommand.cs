using Mediator;

namespace MenuNest.Application.UseCases.Budget.Payments.UpdatePayment;

/// <summary>
/// menunest-209 / R-3 (correcting the original task-7 brief): edits BOTH legs
/// of a payment in one <c>SaveChangesAsync</c> — there is no moment at which
/// half a payment exists.
///
/// <paramref name="CategoryId"/> carries the EXACT SAME three rules as
/// <see cref="MakePayment.MakePaymentCommand.CategoryId"/> (see
/// <see cref="PaymentCategoryRule"/>): required when <c>ToAccountId</c> is a
/// Loan, refused when it is a Credit account, and — when required — must
/// name a real, family-owned, non-Payment envelope. The brief this command
/// was originally written from omitted this field; without it, editing a
/// loan payment would silently drop the category off the outflow leg and
/// reintroduce the Critical menunest-214 defect on every edit.
/// </summary>
public sealed record UpdatePaymentCommand(
    Guid PaymentId, Guid FromAccountId, Guid ToAccountId, decimal Amount,
    DateOnly Date, string? Notes, Guid? CategoryId = null) : ICommand<PaymentDto>;
