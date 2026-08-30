using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget;
using MenuNest.Application.UseCases.Budget.Accounts;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;
using MenuNest.Application.UseCases.Budget.Payments.MakePayment;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Payments;

/// <summary>
/// menunest-204 / menunest-207: paying a Credit or Loan account writes both
/// legs of the payment in one <c>SaveChangesAsync</c>, spends the card's
/// payment envelope down automatically (it is derived, never written to
/// directly — see <see cref="Monthly.PaymentEnvelopeMath"/>), and must never
/// be counted as Income by <see cref="GetMonthlySummaryHandler"/>.
/// </summary>
public class MakePaymentHandlerTests
{
    private static readonly DateOnly D = new(2026, 1, 15);

    private sealed record World(HandlerTestFixture Fx, Guid CashId, Guid CardId, Guid FoodId);

    private static World Seed()
    {
        var fx = new HandlerTestFixture();          // Clock is 2026-01-01 UTC
        var cash = BudgetAccount.Create(fx.Family.Id, "เงินสด", BudgetAccountType.Cash, 0m, 0);
        var card = BudgetAccount.Create(fx.Family.Id, "KBank", BudgetAccountType.Credit, 0m, 1);
        fx.Db.BudgetAccounts.AddRange(cash, card);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "ค่ากิน", 0);
        var food = BudgetCategory.Create(fx.Family.Id, group.Id, "อาหาร", "🍜", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        fx.Db.BudgetCategories.Add(food);

        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, cash.Id, null, 10_000m, D, "Opening balance", fx.User.Id));
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(
            fx.Family.Id, food.Id, 2026, 1, 3_000m));
        fx.Db.SaveChanges();
        return new World(fx, cash.Id, card.Id, food.Id);
    }

    private static MakePaymentHandler Handler(World w) =>
        new(w.Fx.Db, w.Fx.UserProvisioner.Object, new MakePaymentValidator(), w.Fx.Clock);

    // The handler provisions the payment envelope itself on every read
    // (menunest-202), so nothing here has to stage one first.
    private static GetMonthlySummaryHandler SummaryHandler(World w) =>
        new(w.Fx.Db, w.Fx.UserProvisioner.Object, new AllowanceFreezer(w.Fx.Db),
            new PaymentEnvelopeProvisioner(w.Fx.Db), w.Fx.Clock);

    private static async Task<MonthlySummaryDto> SummaryAsync(World w) =>
        await SummaryHandler(w).Handle(new GetMonthlySummaryQuery(2026, 1, "Asia/Bangkok"), default);

    private static void AddTx(World w, Guid accountId, Guid? categoryId, decimal amount)
    {
        w.Fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            w.Fx.Family.Id, accountId, categoryId, amount, D, null, w.Fx.User.Id));
        w.Fx.Db.SaveChanges();
    }

    [Fact]
    public async Task It_writes_both_legs_with_one_shared_PaymentId()
    {
        var w = Seed(); using var _ = w.Fx;
        var dto = await Handler(w).Handle(
            new MakePaymentCommand(w.CashId, w.CardId, 500m, new DateOnly(2026, 1, 20), null, "Asia/Bangkok"), default);

        var legs = w.Fx.Db.BudgetTransactions.Where(t => t.PaymentId == dto.PaymentId).ToList();
        legs.Should().HaveCount(2);
        legs.Single(l => l.AccountId == w.CashId).Amount.Should().Be(-500m);
        legs.Single(l => l.AccountId == w.CardId).Amount.Should().Be(500m);
        legs.Should().OnlyContain(l => l.CategoryId == null);
    }

    [Fact]
    public async Task It_spends_down_the_payment_envelope()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        await Handler(w).Handle(new MakePaymentCommand(w.CashId, w.CardId, 500m, null, null, "Asia/Bangkok"), default);

        var s = await SummaryAsync(w);
        s.Groups.SelectMany(g => g.Categories).Single(e => e.PaymentForAccountId == w.CardId)
            .Available.Should().Be(0m);
        s.Accounts.Single(a => a.Id == w.CardId).Balance.Should().Be(0m);
    }

    // menunest-204: without this, paying your own card reports as money arriving.
    [Fact]
    public async Task A_payment_is_never_counted_as_Income()
    {
        var w = Seed(); using var _ = w.Fx;
        var before = (await SummaryAsync(w)).Income;
        await Handler(w).Handle(new MakePaymentCommand(w.CashId, w.CardId, 500m, null, null, "Asia/Bangkok"), default);
        (await SummaryAsync(w)).Income.Should().Be(before);
    }

    [Fact]
    public async Task Paying_a_loan_works_the_same_way()
    {
        var w = Seed(); using var _ = w.Fx;
        var loan = BudgetAccount.Create(w.Fx.Family.Id, "รถ", BudgetAccountType.Loan, -300_000m, 2);
        w.Fx.Db.BudgetAccounts.Add(loan);
        w.Fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            w.Fx.Family.Id, loan.Id, null, -300_000m, D, "Opening balance", w.Fx.User.Id));
        w.Fx.Db.SaveChanges();

        var rtaBefore = (await SummaryAsync(w)).ReadyToAssign;

        var dto = await Handler(w).Handle(
            new MakePaymentCommand(w.CashId, loan.Id, 5_000m, null, null, "Asia/Bangkok"), default);

        var legs = w.Fx.Db.BudgetTransactions.Where(t => t.PaymentId == dto.PaymentId).ToList();
        legs.Should().HaveCount(2);
        legs.Single(l => l.AccountId == w.CashId).Amount.Should().Be(-5_000m);
        legs.Single(l => l.AccountId == loan.Id).Amount.Should().Be(5_000m);
        legs.Should().OnlyContain(l => l.CategoryId == null);

        // A Loan has no Payment envelope (menunest-206) — its own balance
        // never enters ReadyToAssign (menunest-203/206, IsDebtType), so the
        // in-leg on the loan does not move RTA at all; only the out-leg on
        // Cash (a real, RTA-bearing account) does, by the full payment amount.
        var rtaAfter = (await SummaryAsync(w)).ReadyToAssign;
        rtaAfter.Should().Be(rtaBefore - 5_000m);
    }

    [Fact]
    public async Task Paying_INTO_a_cash_account_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var act = async () => await Handler(w).Handle(
            new MakePaymentCommand(w.CardId, w.CashId, 500m, null, null, "Asia/Bangkok"), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*Credit or Loan*");
    }

    [Fact]
    public async Task Paying_an_account_from_itself_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var act = async () => await Handler(w).Handle(
            new MakePaymentCommand(w.CardId, w.CardId, 500m, null, null, "Asia/Bangkok"), default);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task A_zero_or_negative_amount_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var zero = async () => await Handler(w).Handle(
            new MakePaymentCommand(w.CashId, w.CardId, 0m, null, null, "Asia/Bangkok"), default);
        await zero.Should().ThrowAsync<ValidationException>();

        var negative = async () => await Handler(w).Handle(
            new MakePaymentCommand(w.CashId, w.CardId, -100m, null, null, "Asia/Bangkok"), default);
        await negative.Should().ThrowAsync<ValidationException>();
    }

    // Decision (see task-6-report.md): a Credit account IS allowed to be the
    // FromAccountId — MakePaymentValidator/Handler only restrict ToAccountId
    // to a debt type. Paying one card with another (a balance transfer /
    // cash-advance-style move) is a real thing people do; the source leg is
    // an uncategorised NEGATIVE row on the source card, which
    // PaymentEnvelopeMath.Available ignores (only uncategorised POSITIVE rows
    // count), so it never touches the source card's own payment envelope —
    // it only makes that card's balance more negative, exactly like any other
    // card purchase would.
    [Fact]
    public async Task Paying_a_card_with_another_card_is_allowed_and_does_not_touch_the_source_envelope()
    {
        var w = Seed(); using var _ = w.Fx;
        var card2 = BudgetAccount.Create(w.Fx.Family.Id, "SCB", BudgetAccountType.Credit, 0m, 2);
        w.Fx.Db.BudgetAccounts.Add(card2);
        w.Fx.Db.SaveChanges();

        var sourceAvailableBefore = (await SummaryAsync(w))
            .Groups.SelectMany(g => g.Categories).Single(e => e.PaymentForAccountId == card2.Id)
            .Available;

        var dto = await Handler(w).Handle(
            new MakePaymentCommand(card2.Id, w.CardId, 500m, null, null, "Asia/Bangkok"), default);

        var legs = w.Fx.Db.BudgetTransactions.Where(t => t.PaymentId == dto.PaymentId).ToList();
        legs.Should().HaveCount(2);
        legs.Single(l => l.AccountId == card2.Id).Amount.Should().Be(-500m);
        legs.Single(l => l.AccountId == w.CardId).Amount.Should().Be(500m);

        var s = await SummaryAsync(w);
        s.Accounts.Single(a => a.Id == card2.Id).Balance.Should().Be(-500m);
        var sourceAvailableAfter = s.Groups.SelectMany(g => g.Categories)
            .Single(e => e.PaymentForAccountId == card2.Id).Available;
        sourceAvailableAfter.Should().Be(sourceAvailableBefore,
            "the source card's own payment envelope must not move just because it paid another card");
    }
}
