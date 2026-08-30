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
/// §4.3 — Shortfall is the number that answers issue #112's question: what is
/// still owed and not yet funded. Floors at 0, and is null on anything that
/// isn't a Credit account's Payment envelope (menunest-206: a Loan has no
/// payment envelope, so Shortfall on a Loan would read as its entire
/// outstanding balance forever).
///
/// R-1 — CardSpending is the term that makes the on-screen row trustworthy
/// again: for a Payment envelope, Assigned + Activity alone does not explain
/// the change in Available (a categorised card purchase moves Available with
/// both Assigned and Activity sitting at 0). CardSpending surfaces the missing
/// term so Available == Assigned + CardSpending + Activity holds, month-scoped.
/// </summary>
public class PaymentShortfallTests
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
    public async Task A_fully_funded_card_has_no_shortfall()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        var s = await SummaryAsync(w);

        var env = s.Groups.SelectMany(g => g.Categories).Single(e => e.PaymentForAccountId == w.CardId);
        env.Shortfall.Should().Be(0m);
        s.Accounts.Single(a => a.Id == w.CardId).Shortfall.Should().Be(0m);
    }

    [Fact]
    public async Task Pre_budget_debt_is_the_shortfall()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, null, -20_000m);   // old debt
        AddTx(w, w.CardId, w.FoodId, -500m);  // this month, funded
        var s = await SummaryAsync(w);

        s.Accounts.Single(a => a.Id == w.CardId).Shortfall.Should().Be(20_000m);
    }

    [Fact]
    public async Task A_cash_account_has_no_shortfall_at_all()
    {
        var w = Seed(); using var _ = w.Fx;
        var s = await SummaryAsync(w);
        s.Accounts.Single(a => a.Id == w.CashId).Shortfall.Should().BeNull();
    }

    [Fact]
    public async Task A_loan_has_no_shortfall_because_it_has_no_payment_envelope()
    {
        var w = Seed(); using var _ = w.Fx;
        var loan = BudgetAccount.Create(w.Fx.Family.Id, "รถ", BudgetAccountType.Loan, 0m, 2);
        w.Fx.Db.BudgetAccounts.Add(loan);
        w.Fx.Db.SaveChanges();
        AddTx(w, loan.Id, null, -300_000m);

        var s = await SummaryAsync(w);
        s.Accounts.Single(a => a.Id == loan.Id).Shortfall
            .Should().BeNull("menunest-206 — a loan must not read ขาดอีก 300,000 forever");
    }

    [Fact]
    public async Task An_ordinary_envelope_has_no_payment_fields()
    {
        var w = Seed(); using var _ = w.Fx;
        var s = await SummaryAsync(w);
        var food = s.Groups.SelectMany(g => g.Categories).Single(e => e.CategoryId == w.FoodId);
        food.PaymentForAccountId.Should().BeNull();
        food.Shortfall.Should().BeNull();
        food.CardSpending.Should().BeNull();
    }

    // ---------- R-1: CardSpending ----------

    [Fact]
    public async Task An_ordinary_envelope_never_carries_card_spending()
    {
        // Covered above too, but named for R-1 traceability.
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        var s = await SummaryAsync(w);
        var food = s.Groups.SelectMany(g => g.Categories).Single(e => e.CategoryId == w.FoodId);
        food.CardSpending.Should().BeNull();
    }

    [Fact]
    public async Task Card_spending_is_positive_for_a_categorised_purchase()
    {
        var w = Seed(); using var _ = w.Fx;
        AddTx(w, w.CardId, w.FoodId, -500m);
        var s = await SummaryAsync(w);

        var env = s.Groups.SelectMany(g => g.Categories).Single(e => e.PaymentForAccountId == w.CardId);
        env.CardSpending.Should().Be(500m, "a card purchase is positive spending, the mirror of the negative tx amount");
    }

    /// <summary>
    /// The identity from R-1: Available == Assigned + CardSpending + Activity.
    /// This only holds as an ABSOLUTE-value identity when there is no
    /// rollover from a prior month (Available carried in from before this
    /// month is 0) — which is exactly this fixture's shape: Seed() opens the
    /// card fresh, with no assignments or transactions before month D. Under
    /// that condition it holds exactly, not just as a month-over-month delta,
    /// because Available itself is entirely explained by this month's terms.
    /// Reuses the whole event sequence from
    /// CreditRtaInvariantTests.A_whole_sequence_on_one_card_leaves_it_where_it_started
    /// so the check runs against non-trivial, independently-verified numbers.
    /// </summary>
    [Fact]
    public async Task Available_equals_assigned_plus_card_spending_plus_activity_for_the_month()
    {
        var w = Seed(); using var _ = w.Fx;

        AddTx(w, w.CardId, null, -20_000m);      // 1. opening debt (uncategorised, negative — ignored by both terms)
        AddTx(w, w.CardId, w.FoodId, -500m);     // 2. purchase
        AddTx(w, w.CardId, w.FoodId, 500m);      // 3. refund
        AddTx(w, w.CardId, w.FoodId, -500m);     // 4. purchase again
        AddTx(w, w.CardId, null, -300m);         // 5. cash advance (uncategorised, negative — ignored)
        var payId = Guid.NewGuid();              // 6. pay 500
        w.Fx.Db.BudgetTransactions.AddRange(
            BudgetTransaction.CreatePaymentLeg(w.Fx.Family.Id, w.CashId, -500m, D, null, w.Fx.User.Id, payId),
            BudgetTransaction.CreatePaymentLeg(w.Fx.Family.Id, w.CardId, 500m, D, null, w.Fx.User.Id, payId));
        w.Fx.Db.SaveChanges();

        var s = await SummaryAsync(w);
        var env = s.Groups.SelectMany(g => g.Categories).Single(e => e.PaymentForAccountId == w.CardId);

        // From CreditRtaInvariantTests's derivation: Available = 0, Assigned = 0.
        // CardSpending = −Σ(categorised) = −(−500+500−500) = 500.
        // Activity = −Σ(uncategorised positive this month) = −(500) = −500.
        env.Assigned.Should().Be(0m);
        env.CardSpending.Should().Be(500m);
        env.Activity.Should().Be(-500m);
        env.Available.Should().Be(0m);

        env.Available.Should().Be(env.Assigned + env.CardSpending!.Value + env.Activity,
            "R-1: the payment envelope's Available must be explained by its own three terms, " +
            "the same way an ordinary envelope's Assigned+Activity explains its Available");
    }
}
