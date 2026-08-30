using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget;
using MenuNest.Application.UseCases.Budget.Accounts;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

/// <summary>
/// Spec §4.4 — the acceptance test. No activity on a Credit account may change
/// Ready to Assign. Not one case, the payment included. If any of these comes
/// out non-zero the model is wrong.
/// </summary>
public class CreditRtaInvariantTests
{
    private const string Bkk = "Asia/Bangkok";
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

    // The handler provisions the payment envelope itself on every read
    // (menunest-202), so nothing here has to stage one first.
    private static GetMonthlySummaryHandler Build(World w) =>
        new(w.Fx.Db, w.Fx.UserProvisioner.Object, new AllowanceFreezer(w.Fx.Db),
            new PaymentEnvelopeProvisioner(w.Fx.Db), w.Fx.Clock);

    private static async Task<MonthlySummaryDto> SummaryAsync(World w) =>
        await Build(w).Handle(new GetMonthlySummaryQuery(2026, 1, Bkk), default);

    private static async Task<decimal> RtaAsync(World w) => (await SummaryAsync(w)).ReadyToAssign;

    private static void AddTx(World w, Guid accountId, Guid? categoryId, decimal amount)
    {
        w.Fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            w.Fx.Family.Id, accountId, categoryId, amount, D, null, w.Fx.User.Id));
        w.Fx.Db.SaveChanges();
    }

    [Fact]
    public async Task Baseline_is_seven_thousand()
    {
        var w = Seed(); using var _ = w.Fx;
        (await RtaAsync(w)).Should().Be(7_000m);
    }

    [Fact]
    public async Task A_categorised_card_purchase_does_not_move_it()
    {
        var w = Seed(); using var _ = w.Fx;
        var before = await RtaAsync(w);
        AddTx(w, w.CardId, w.FoodId, -500m);
        (await RtaAsync(w)).Should().Be(before);
    }

    [Fact]
    public async Task A_categorised_refund_does_not_move_it()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        var before = await RtaAsync(w);
        AddTx(w, w.CardId, w.FoodId, 500m);
        (await RtaAsync(w)).Should().Be(before);
    }

    [Fact]
    public async Task An_uncategorised_card_purchase_does_not_move_it()
    {
        var w = Seed(); using var _ = w.Fx;
        var before = await RtaAsync(w);
        AddTx(w, w.CardId, null, -500m);
        (await RtaAsync(w)).Should().Be(before);
    }

    [Fact]
    public async Task A_cards_opening_debt_does_not_move_it()
    {
        var w = Seed(); using var _ = w.Fx;
        var before = await RtaAsync(w);
        AddTx(w, w.CardId, null, -20_000m);
        (await RtaAsync(w)).Should().Be(before, "pre-budget debt sits outside the budget");
    }

    [Fact]
    public async Task A_payment_does_not_move_it()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        var before = await RtaAsync(w);
        var payId = Guid.NewGuid();
        w.Fx.Db.BudgetTransactions.AddRange(
            BudgetTransaction.CreatePaymentLeg(w.Fx.Family.Id, w.CashId, -500m, D, null, w.Fx.User.Id, payId),
            BudgetTransaction.CreatePaymentLeg(w.Fx.Family.Id, w.CardId, 500m, D, null, w.Fx.User.Id, payId));
        w.Fx.Db.SaveChanges();
        (await RtaAsync(w)).Should().Be(before, "you spend money you had already set aside");
    }

    [Fact]
    public async Task A_loan_is_out_of_it_too()
    {
        var w = Seed(); using var _ = w.Fx;
        var before = await RtaAsync(w);
        var loan = BudgetAccount.Create(w.Fx.Family.Id, "รถ", BudgetAccountType.Loan, 0m, 2);
        w.Fx.Db.BudgetAccounts.Add(loan);
        w.Fx.Db.SaveChanges();
        AddTx(w, loan.Id, null, -300_000m);
        (await RtaAsync(w)).Should().Be(before, "menunest-206 — not ตั้งงบเกิน -293,000");
    }

    [Fact]
    public async Task The_payment_envelope_tracks_the_card()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        var s = await SummaryAsync(w);

        // EnvelopeDto does not carry PaymentForAccountId until Task 4, so the
        // envelope is located through the entity the provisioner created.
        var envelopeId = w.Fx.Db.BudgetCategories
            .Single(c => c.PaymentForAccountId == w.CardId).Id;

        var env = s.Groups.SelectMany(g => g.Categories).Single(e => e.CategoryId == envelopeId);
        env.Available.Should().Be(500m);
        s.Accounts.Single(a => a.Id == w.CardId).Balance.Should().Be(-500m);
    }

    [Fact]
    public async Task Paying_the_card_is_the_payment_envelopes_activity()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        var payId = Guid.NewGuid();
        w.Fx.Db.BudgetTransactions.AddRange(
            BudgetTransaction.CreatePaymentLeg(w.Fx.Family.Id, w.CashId, -500m, D, null, w.Fx.User.Id, payId),
            BudgetTransaction.CreatePaymentLeg(w.Fx.Family.Id, w.CardId, 500m, D, null, w.Fx.User.Id, payId));
        w.Fx.Db.SaveChanges();

        var s = await SummaryAsync(w);
        var envelopeId = w.Fx.Db.BudgetCategories
            .Single(c => c.PaymentForAccountId == w.CardId).Id;
        var env = s.Groups.SelectMany(g => g.Categories).Single(e => e.CategoryId == envelopeId);

        env.Activity.Should().Be(-500m, "money that left the envelope shows negative, like any spending");
        env.Available.Should().Be(0m);
    }
}
