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

    /// <summary>
    /// The identity that is ALWAYS true (per the fix report) is the DELTA form,
    /// not the absolute one above — <c>Available</c> is cumulative across every
    /// month a card has existed, while Assigned/CardSpending/Activity are each
    /// scoped to the selected month alone. The test above only ever exercises a
    /// card's first month, where the prior-month carry-in is trivially 0 and the
    /// absolute form happens to hold too — that says nothing about month 2
    /// onward, which is every month in real use. This test runs a card through
    /// TWO months and checks the delta form, which is the property R-1 exists to
    /// guarantee (a payment envelope's Available must never move for a reason
    /// the row's own three numbers don't add up to).
    /// </summary>
    [Fact]
    public async Task Card_spending_delta_explains_the_change_in_available_across_months()
    {
        var w = Seed(); using var _ = w.Fx;
        var feb = new DateOnly(2026, 2, 15);

        // Provision the payment envelope before staging month-1 data — the
        // handler provisions lazily on read (menunest-202), and nothing
        // creates it before the first Handle call.
        await Build(w).Handle(new GetMonthlySummaryQuery(2026, 1, Bkk), default);
        var envelopeId = w.Fx.Db.BudgetCategories.Single(c => c.PaymentForAccountId == w.CardId).Id;

        // ---- Month 1 (January): assign 1,000; one 400 categorised purchase; one 300 payment.
        w.Fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(w.Fx.Family.Id, envelopeId, 2026, 1, 1_000m));
        w.Fx.Db.SaveChanges();
        AddTx(w, w.CardId, w.FoodId, -400m);
        var pay1 = Guid.NewGuid();
        w.Fx.Db.BudgetTransactions.AddRange(
            BudgetTransaction.CreatePaymentLeg(w.Fx.Family.Id, w.CashId, -300m, D, null, w.Fx.User.Id, pay1),
            BudgetTransaction.CreatePaymentLeg(w.Fx.Family.Id, w.CardId, 300m, D, null, w.Fx.User.Id, pay1));
        w.Fx.Db.SaveChanges();

        var s1 = await Build(w).Handle(new GetMonthlySummaryQuery(2026, 1, Bkk), default);
        var env1 = s1.Groups.SelectMany(g => g.Categories).Single(e => e.CategoryId == envelopeId);

        // By hand, §4.2/R-1 (all cumulative through end of January, which is
        // ALL of it — this is the card's first month):
        //   assignedToDate = 1,000
        //   Σ(categorised)        = −400                     (the purchase)
        //   Σ(uncategorised, pos) = +300                      (the payment leg on the card)
        //   Available1 = 1,000 − (−400) − 300 = 1,100
        env1.Assigned.Should().Be(1_000m);
        env1.CardSpending.Should().Be(400m);
        env1.Activity.Should().Be(-300m);
        env1.Available.Should().Be(1_100m);

        // ---- Month 2 (February): assign 800 more; one 600 categorised purchase; one 500 payment.
        w.Fx.Db.MonthlyAssignments.Add(MonthlyAssignment.Create(w.Fx.Family.Id, envelopeId, 2026, 2, 800m));
        w.Fx.Db.SaveChanges();
        w.Fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            w.Fx.Family.Id, w.CardId, w.FoodId, -600m, feb, null, w.Fx.User.Id));
        var pay2 = Guid.NewGuid();
        w.Fx.Db.BudgetTransactions.AddRange(
            BudgetTransaction.CreatePaymentLeg(w.Fx.Family.Id, w.CashId, -500m, feb, null, w.Fx.User.Id, pay2),
            BudgetTransaction.CreatePaymentLeg(w.Fx.Family.Id, w.CardId, 500m, feb, null, w.Fx.User.Id, pay2));
        w.Fx.Db.SaveChanges();

        var s2 = await Build(w).Handle(new GetMonthlySummaryQuery(2026, 2, Bkk), default);
        var env2 = s2.Groups.SelectMany(g => g.Categories).Single(e => e.CategoryId == envelopeId);

        // By hand, cumulative through end of February (January's rows plus
        // February's):
        //   assignedToDate = 1,000 + 800 = 1,800
        //   Σ(categorised)        = −400 − 600 = −1,000
        //   Σ(uncategorised, pos) = 300 + 500 = 800
        //   Available2 = 1,800 − (−1,000) − 800 = 2,000
        // February-only (what EnvelopeNumbers reports as Assigned/Activity/CardSpending):
        env2.Assigned.Should().Be(800m);
        env2.CardSpending.Should().Be(600m);
        env2.Activity.Should().Be(-500m);
        env2.Available.Should().Be(2_000m);

        // The DELTA identity — the one that is generally, always true (this is
        // what the DTO comment on EnvelopeDto.CardSpending now states):
        //   Available2 − Available1 = 2,000 − 1,100 = 900
        //   Assigned2 + CardSpending2 + Activity2 = 800 + 600 − 500 = 900
        (env2.Available - env1.Available).Should().Be(
            env2.Assigned + env2.CardSpending!.Value + env2.Activity,
            "R-1: from month 2 onward only the DELTA form holds — a card carrying a balance " +
            "from a prior month must still have its month-over-month CHANGE in Available " +
            "explained by that month's own Assigned + CardSpending + Activity");

        // And, made concrete: the ABSOLUTE form (which DID hold for month 1
        // above, and is asserted by Available_equals_assigned_plus_card_spending_plus_activity_for_the_month)
        // no longer holds by month 2 — this is exactly the gap the delta form
        // exists to close, not a form that was ever meant to survive a second month.
        env2.Available.Should().NotBe(env2.Assigned + env2.CardSpending!.Value + env2.Activity);
    }
}
