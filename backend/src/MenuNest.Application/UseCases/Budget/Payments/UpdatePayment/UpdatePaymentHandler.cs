using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Monthly;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Payments.UpdatePayment;

/// <summary>
/// menunest-209 / R-3: rewrites both legs of a payment in one
/// <c>SaveChangesAsync</c> — reverses both old balances, then applies both
/// new ones, so there is no moment at which half a payment exists. The two
/// legs are told apart by sign (the outflow leg is always negative, the
/// inflow leg always positive) rather than by which account they are
/// currently on, since <see cref="UpdatePaymentCommand.FromAccountId"/> /
/// <see cref="UpdatePaymentCommand.ToAccountId"/> may move either leg to a
/// different account.
///
/// The category rule is the identical <see cref="PaymentCategoryRule"/> used
/// by <see cref="MakePayment.MakePaymentHandler"/> — an edit must never be
/// able to check it differently and silently drop the category off a Loan's
/// outflow leg (that is exactly how menunest-214 could come back).
/// </summary>
public sealed class UpdatePaymentHandler : ICommandHandler<UpdatePaymentCommand, PaymentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<UpdatePaymentCommand> _v;

    public UpdatePaymentHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<UpdatePaymentCommand> v)
    { _db = db; _users = users; _v = v; }

    public async ValueTask<PaymentDto> Handle(UpdatePaymentCommand c, CancellationToken ct)
    {
        await _v.ValidateAndThrowAsync(c, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var legs = await _db.BudgetTransactions
            .Where(t => t.PaymentId == c.PaymentId && t.FamilyId == familyId)
            .ToListAsync(ct);
        if (legs.Count != 2)
            throw new DomainException("Payment not found.");

        var outLeg = legs.Single(l => l.Amount < 0);
        var inLeg = legs.Single(l => l.Amount > 0);

        // Reverse the OLD balance effect on whichever accounts the legs were
        // actually on — these may not be c.FromAccountId/c.ToAccountId if the
        // edit is moving the payment to different accounts.
        var oldFromAcc = await _db.BudgetAccounts.FirstOrDefaultAsync(
            a => a.Id == outLeg.AccountId && a.FamilyId == familyId, ct)
            ?? throw new DomainException("Account not found.");
        var oldToAcc = await _db.BudgetAccounts.FirstOrDefaultAsync(
            a => a.Id == inLeg.AccountId && a.FamilyId == familyId, ct)
            ?? throw new DomainException("Account not found.");
        oldFromAcc.AdjustBalance(-outLeg.Amount);
        oldToAcc.AdjustBalance(-inLeg.Amount);

        // Re-resolve the (possibly new) accounts and re-validate exactly as
        // MakePaymentHandler — the pair may have changed to a different Cash
        // account, a different card, or even a different debt entirely.
        var from = await _db.BudgetAccounts.FirstOrDefaultAsync(
            a => a.Id == c.FromAccountId && a.FamilyId == familyId, ct)
            ?? throw new DomainException("Paying account not found.");
        var to = await _db.BudgetAccounts.FirstOrDefaultAsync(
            a => a.Id == c.ToAccountId && a.FamilyId == familyId, ct)
            ?? throw new DomainException("Account being paid not found.");

        if (!PaymentEnvelopeMath.IsDebtType(to.Type))
            throw new DomainException("Only a Credit or Loan account can be paid.");
        if (from.Type == BudgetAccountType.Loan)
            throw new DomainException("A Loan account cannot be the paying account.");

        var category = await PaymentCategoryRule.ResolveAsync(_db, to.Type, c.CategoryId, familyId, ct);

        outLeg.Update(from.Id, category?.Id, -c.Amount, c.Date, c.Notes);
        inLeg.Update(to.Id, null, c.Amount, c.Date, c.Notes);

        from.AdjustBalance(-c.Amount);
        to.AdjustBalance(c.Amount);

        await _db.SaveChangesAsync(ct); // ONE unit of work — never half a pair

        return new PaymentDto(c.PaymentId, from.Id, from.Name, to.Id, to.Name,
            c.Amount, c.Date, outLeg.Notes);
    }
}
