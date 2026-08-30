using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Monthly;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Payments.MakePayment;

/// <summary>
/// menunest-204 / menunest-207. Writes both legs of a payment in a single
/// <c>SaveChangesAsync</c> — there is no moment at which half a payment
/// exists. The card-side leg is an uncategorised positive amount, which
/// <see cref="PaymentEnvelopeMath"/> already subtracts from a payment
/// envelope's Available — nothing here writes to the envelope directly.
///
/// <see cref="MakePaymentCommand.FromAccountId"/> is deliberately NOT
/// restricted to a non-debt type: paying one Credit card with another (a
/// balance-transfer / cash-advance style move) is allowed. The source leg is
/// an uncategorised NEGATIVE row on the source card, and
/// <see cref="PaymentEnvelopeMath.Available"/> only ever subtracts
/// uncategorised POSITIVE rows — so it never touches the source card's own
/// payment envelope, it only widens that card's own debt, exactly like any
/// other card purchase would (see task-6-report.md).
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

        var tz = BudgetTimeZone.Resolve(c.TimeZoneId);
        var date = c.Date ?? DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz));

        var paymentId = Guid.NewGuid();
        var outLeg = BudgetTransaction.CreatePaymentLeg(
            familyId, from.Id, -c.Amount, date, c.Notes, user.Id, paymentId);
        var inLeg = BudgetTransaction.CreatePaymentLeg(
            familyId, to.Id, c.Amount, date, c.Notes, user.Id, paymentId);

        _db.BudgetTransactions.AddRange(outLeg, inLeg);
        from.AdjustBalance(-c.Amount);   // keep the cached copies true
        to.AdjustBalance(c.Amount);
        await _db.SaveChangesAsync(ct);  // ONE unit of work — never half a pair

        return new PaymentDto(paymentId, from.Id, from.Name, to.Id, to.Name,
            c.Amount, date, outLeg.Notes);
    }
}
