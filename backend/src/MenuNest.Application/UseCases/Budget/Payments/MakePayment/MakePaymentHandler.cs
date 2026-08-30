using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Monthly;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Payments.MakePayment;

/// <summary>
/// menunest-204 / menunest-207 / menunest-214. Writes both legs of a payment in
/// a single <c>SaveChangesAsync</c> — there is no moment at which half a
/// payment exists.
///
/// <b>Credit</b>: both legs stay uncategorised. The card-side leg is an
/// uncategorised positive amount, which <see cref="PaymentEnvelopeMath"/>
/// already subtracts from the card's Payment envelope — nothing here writes
/// to that envelope directly, and categorising the from-leg too would spend
/// the payment against a second envelope on top of that derivation.
///
/// <b>Loan</b> (menunest-214, correcting menunest-207): a Loan has no Payment
/// envelope of its own (menunest-206), so unlike a card, NOTHING falls by
/// derivation when a loan is paid. The from-leg's Envelope
/// (<see cref="MakePaymentCommand.CategoryId"/>) is therefore REQUIRED — it is
/// the only thing a loan payment ever spends. Without it the instalment drains
/// Ready to Assign every month while the Envelope meant to fund it is never
/// touched (see docs/adr/menunest-214-a-loan-payment-carries-the-envelope-that-funds-it.md).
/// The category lookup excludes a Payment envelope (<c>PaymentForAccountId != null</c>):
/// a card's Payment envelope is derived solely from THAT card's own rows
/// (<see cref="PaymentEnvelopeMath"/>), so a categorised row on the Loan's
/// from-leg would land on it and vanish from every derivation — reproducing
/// the exact original defect one level down.
///
/// <see cref="MakePaymentCommand.FromAccountId"/> may be a Credit account
/// (paying one card with another — a balance-transfer / cash-advance style
/// move — is allowed; the source leg is an uncategorised NEGATIVE row, which
/// <see cref="PaymentEnvelopeMath.Available"/> never subtracts, so it only
/// widens the source card's own debt, exactly like any other card purchase).
/// It may NOT be a Loan: a Loan's balance is not itself spendable money, so
/// paying one loan "from" another would only write a meaningless uncategorised
/// row with nothing behind it.
/// </summary>
public sealed class MakePaymentHandler : ICommandHandler<MakePaymentCommand, PaymentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<MakePaymentCommand> _v;
    private readonly IClock _clock;

    public MakePaymentHandler(IApplicationDbContext db, IUserProvisioner users,
        IValidator<MakePaymentCommand> v, IClock clock)
    { _db = db; _users = users; _v = v; _clock = clock; }

    public async ValueTask<PaymentDto> Handle(MakePaymentCommand c, CancellationToken ct)
    {
        await _v.ValidateAndThrowAsync(c, ct);
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        var from = await _db.BudgetAccounts.FirstOrDefaultAsync(
            a => a.Id == c.FromAccountId && a.FamilyId == familyId, ct)
            ?? throw new DomainException("Paying account not found.");
        var to = await _db.BudgetAccounts.FirstOrDefaultAsync(
            a => a.Id == c.ToAccountId && a.FamilyId == familyId, ct)
            ?? throw new DomainException("Account being paid not found.");

        // menunest-207: only a debt account is ever paid. Paying a Cash account
        // would be a transfer, which MenuNest deliberately does not have.
        if (!PaymentEnvelopeMath.IsDebtType(to.Type))
            throw new DomainException("Only a Credit or Loan account can be paid.");

        // A Loan's balance is not spendable money — paying one loan "from"
        // another would write a meaningless row with nothing real behind it.
        if (from.Type == BudgetAccountType.Loan)
            throw new DomainException("A Loan account cannot be the paying account.");

        // menunest-214: the Envelope funding the instalment lives on the
        // from-leg only, and its requiredness is the mirror image of the
        // target account's own derivation.
        BudgetCategory? category = null;
        if (to.Type == BudgetAccountType.Loan)
        {
            if (c.CategoryId is not { } categoryId)
                throw new DomainException("Paying a Loan requires an Envelope to fund it.");
            category = await _db.BudgetCategories.FirstOrDefaultAsync(
                x => x.Id == categoryId && x.FamilyId == familyId && x.PaymentForAccountId == null, ct)
                ?? throw new DomainException(
                    "Category not found, or is a Payment envelope — a Payment envelope cannot fund another debt's payment.");
        }
        else if (c.CategoryId is not null)
        {
            // to.Type == Credit here (the IsDebtType guard above already
            // narrowed it to Credit or Loan).
            throw new DomainException(
                "Paying a Credit card cannot be categorised — its Payment envelope already falls by derivation.");
        }

        var tz = BudgetTimeZone.Resolve(c.TimeZoneId);
        var date = c.Date ?? DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz));

        var paymentId = Guid.NewGuid();
        var outLeg = BudgetTransaction.CreatePaymentLeg(
            familyId, from.Id, category?.Id, -c.Amount, date, c.Notes, user.Id, paymentId);
        var inLeg = BudgetTransaction.CreatePaymentLeg(
            familyId, to.Id, null, c.Amount, date, c.Notes, user.Id, paymentId);

        _db.BudgetTransactions.AddRange(outLeg, inLeg);
        from.AdjustBalance(-c.Amount);   // keep the cached copies true
        to.AdjustBalance(c.Amount);
        await _db.SaveChangesAsync(ct);  // ONE unit of work — never half a pair

        return new PaymentDto(paymentId, from.Id, from.Name, to.Id, to.Name,
            c.Amount, date, outLeg.Notes);
    }
}
