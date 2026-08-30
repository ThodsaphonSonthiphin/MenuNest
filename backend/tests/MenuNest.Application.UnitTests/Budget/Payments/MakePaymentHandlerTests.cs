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
/// menunest-204 / menunest-207 / menunest-214: paying a Credit or Loan account
/// writes both legs of the payment in one <c>SaveChangesAsync</c>, must never
/// be counted as Income by <see cref="GetMonthlySummaryHandler"/>, and must
/// leave Ready to Assign correctly accounted for on BOTH debt types:
/// - Credit: nothing is written to the envelope; the card's Payment envelope
///   falls by derivation (see <see cref="Monthly.PaymentEnvelopeMath"/>).
/// - Loan: a Loan has no Payment envelope of its own (menunest-206), so the
///   from-leg carries the ordinary Envelope that funds the instalment — see
///   docs/adr/menunest-214-a-loan-payment-carries-the-envelope-that-funds-it.md.
/// </summary>
public class MakePaymentHandlerTests
{
    private static readonly DateOnly D = new(2026, 1, 15);

    private static MakePaymentHandler Handler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new MakePaymentValidator(), fx.Clock);

    // The handler provisions the payment envelope itself on every read
    // (menunest-202), so nothing here has to stage one first.
    private static GetMonthlySummaryHandler SummaryHandler(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db),
            new PaymentEnvelopeProvisioner(fx.Db), fx.Clock);

    private static async Task<MonthlySummaryDto> SummaryAsync(HandlerTestFixture fx) =>
        await SummaryHandler(fx).Handle(new GetMonthlySummaryQuery(2026, 1, "Asia/Bangkok"), default);

    // ---------- Credit-card scenarios ----------

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
        var dto = await Handler(w.Fx).Handle(
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
        await Handler(w.Fx).Handle(new MakePaymentCommand(w.CashId, w.CardId, 500m, null, null, "Asia/Bangkok"), default);

        var s = await SummaryAsync(w.Fx);
        s.Groups.SelectMany(g => g.Categories).Single(e => e.PaymentForAccountId == w.CardId)
            .Available.Should().Be(0m);
        s.Accounts.Single(a => a.Id == w.CardId).Balance.Should().Be(0m);
    }

    // menunest-204: without this, paying your own card reports as money arriving.
    [Fact]
    public async Task A_payment_is_never_counted_as_Income()
    {
        var w = Seed(); using var _ = w.Fx;
        var before = (await SummaryAsync(w.Fx)).Income;
        await Handler(w.Fx).Handle(new MakePaymentCommand(w.CashId, w.CardId, 500m, null, null, "Asia/Bangkok"), default);
        (await SummaryAsync(w.Fx)).Income.Should().Be(before);
    }

    [Fact]
    public async Task Paying_INTO_a_cash_account_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var act = async () => await Handler(w.Fx).Handle(
            new MakePaymentCommand(w.CardId, w.CashId, 500m, null, null, "Asia/Bangkok"), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*Credit or Loan*");
    }

    [Fact]
    public async Task Paying_an_account_from_itself_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var act = async () => await Handler(w.Fx).Handle(
            new MakePaymentCommand(w.CardId, w.CardId, 500m, null, null, "Asia/Bangkok"), default);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task A_zero_or_negative_amount_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var zero = async () => await Handler(w.Fx).Handle(
            new MakePaymentCommand(w.CashId, w.CardId, 0m, null, null, "Asia/Bangkok"), default);
        await zero.Should().ThrowAsync<ValidationException>();

        var negative = async () => await Handler(w.Fx).Handle(
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

        var sourceAvailableBefore = (await SummaryAsync(w.Fx))
            .Groups.SelectMany(g => g.Categories).Single(e => e.PaymentForAccountId == card2.Id)
            .Available;

        var dto = await Handler(w.Fx).Handle(
            new MakePaymentCommand(card2.Id, w.CardId, 500m, null, null, "Asia/Bangkok"), default);

        var legs = w.Fx.Db.BudgetTransactions.Where(t => t.PaymentId == dto.PaymentId).ToList();
        legs.Should().HaveCount(2);
        legs.Single(l => l.AccountId == card2.Id).Amount.Should().Be(-500m);
        legs.Single(l => l.AccountId == w.CardId).Amount.Should().Be(500m);

        var s = await SummaryAsync(w.Fx);
        s.Accounts.Single(a => a.Id == card2.Id).Balance.Should().Be(-500m);
        var sourceAvailableAfter = s.Groups.SelectMany(g => g.Categories)
            .Single(e => e.PaymentForAccountId == card2.Id).Available;
        sourceAvailableAfter.Should().Be(sourceAvailableBefore,
            "the source card's own payment envelope must not move just because it paid another card");
    }

    [Fact]
    public async Task A_card_payment_with_a_category_is_refused()
    {
        var w = Seed(); using var _ = w.Fx;
        var act = async () => await Handler(w.Fx).Handle(
            new MakePaymentCommand(w.CashId, w.CardId, 500m, null, null, "Asia/Bangkok", w.FoodId), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*cannot be categorised*");
    }

    // ---------- Loan scenarios (menunest-214) ----------

    private sealed record LoanWorld(HandlerTestFixture Fx, Guid CashId, Guid LoanId, Guid LoanEnvelopeId);

    // Numbers match the reviewer's independently-confirmed reproduction exactly
    // (cash 100,000 · "ผ่อนรถ" envelope 8,000 assigned/available · loan −300,000 ·
    // RTA before = 92,000) so the fix can be checked against a known-good trace.
    private static LoanWorld SeedLoan()
    {
        var fx = new HandlerTestFixture();           // Clock is 2026-01-01 UTC
        var cash = BudgetAccount.Create(fx.Family.Id, "เงินสด", BudgetAccountType.Cash, 0m, 0);
        var loan = BudgetAccount.Create(fx.Family.Id, "รถ", BudgetAccountType.Loan, 0m, 1);
        fx.Db.BudgetAccounts.AddRange(cash, loan);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "หนี้สิน", 0);
        var envelope = BudgetCategory.Create(fx.Family.Id, group.Id, "ผ่อนรถ", "🚗", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        fx.Db.BudgetCategories.Add(envelope);

        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, cash.Id, null, 100_000m, D, "Opening balance", fx.User.Id));
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, loan.Id, null, -300_000m, D, "Opening balance", fx.User.Id));
        fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(
            fx.Family.Id, envelope.Id, 2026, 1, 8_000m));
        fx.Db.SaveChanges();
        return new LoanWorld(fx, cash.Id, loan.Id, envelope.Id);
    }

    [Fact]
    public async Task Paying_a_loan_works_the_same_way()
    {
        var w = SeedLoan(); using var _ = w.Fx;

        var before = await SummaryAsync(w.Fx);
        before.ReadyToAssign.Should().Be(92_000m, "sanity check against the reviewer's confirmed trace");

        var dto = await Handler(w.Fx).Handle(
            new MakePaymentCommand(w.CashId, w.LoanId, 8_000m, null, null, "Asia/Bangkok", w.LoanEnvelopeId), default);

        var legs = w.Fx.Db.BudgetTransactions.Where(t => t.PaymentId == dto.PaymentId).ToList();
        legs.Should().HaveCount(2);
        var outLeg = legs.Single(l => l.AccountId == w.CashId);
        var inLeg = legs.Single(l => l.AccountId == w.LoanId);
        outLeg.Amount.Should().Be(-8_000m);
        inLeg.Amount.Should().Be(8_000m);
        // menunest-214: the Envelope is on the from-leg only — the in-leg,
        // landing on the debt account itself, is never categorised.
        outLeg.CategoryId.Should().Be(w.LoanEnvelopeId);
        inLeg.CategoryId.Should().BeNull();

        var after = await SummaryAsync(w.Fx);
        // The Envelope is what actually got spent — Available drops by the
        // full instalment, to 0 (assigned 8,000 − categorised 8,000).
        after.Groups.SelectMany(g => g.Categories).Single(e => e.CategoryId == w.LoanEnvelopeId)
            .Available.Should().Be(0m);
        // A Loan has no Payment envelope (menunest-206) and its own balance
        // never enters ReadyToAssign (menunest-203/206, IsDebtType) — so RTA
        // must be explained ENTIRELY by the from-leg's Envelope now holding
        // the money, exactly like paying a Credit card. Symmetric with the
        // card case: RTA is unchanged by the act of paying.
        after.ReadyToAssign.Should().Be(before.ReadyToAssign);
    }

    [Fact]
    public async Task A_loan_payment_without_a_category_is_refused()
    {
        var w = SeedLoan(); using var _ = w.Fx;
        var act = async () => await Handler(w.Fx).Handle(
            new MakePaymentCommand(w.CashId, w.LoanId, 8_000m, null, null, "Asia/Bangkok"), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*Envelope*");
    }

    // menunest-214 review round 2: a card's own Payment envelope is derived
    // solely from THAT card's rows (PaymentEnvelopeMath) — a categorised row
    // elsewhere (here, the Loan's from-leg on Cash) never reaches that
    // derivation, so funding a loan payment with one would reproduce the
    // original defect one level down: the envelope stays pinned and RTA
    // falls by the instalment again.
    [Fact]
    public async Task Paying_a_loan_with_a_cards_payment_envelope_is_refused()
    {
        var w = SeedLoan(); using var _ = w.Fx;
        var card = BudgetAccount.Create(w.Fx.Family.Id, "KBank", BudgetAccountType.Credit, 0m, 2);
        w.Fx.Db.BudgetAccounts.Add(card);
        w.Fx.Db.SaveChanges();
        await SummaryAsync(w.Fx); // provisions the card's Payment envelope (menunest-202)
        var cardEnvelopeId = w.Fx.Db.BudgetCategories.Single(x => x.PaymentForAccountId == card.Id).Id;

        var act = async () => await Handler(w.Fx).Handle(
            new MakePaymentCommand(w.CashId, w.LoanId, 8_000m, null, null, "Asia/Bangkok", cardEnvelopeId), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*Payment envelope*");
    }

    [Fact]
    public async Task A_loan_cannot_be_the_paying_account()
    {
        var w = SeedLoan(); using var _ = w.Fx;
        var loan2 = BudgetAccount.Create(w.Fx.Family.Id, "บ้าน", BudgetAccountType.Loan, 0m, 2);
        w.Fx.Db.BudgetAccounts.Add(loan2);
        w.Fx.Db.SaveChanges();

        var act = async () => await Handler(w.Fx).Handle(
            new MakePaymentCommand(loan2.Id, w.LoanId, 1_000m, null, null, "Asia/Bangkok", w.LoanEnvelopeId), default);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*Loan account cannot be the paying account*");
    }
}
