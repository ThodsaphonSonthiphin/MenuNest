using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class GetMonthlySummaryDerivedBalanceTests
{
    // The app's one real time zone (menunest-189) — every user is in Thailand.
    private const string Bkk = "Asia/Bangkok";

    private static GetMonthlySummaryHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), new PaymentEnvelopeProvisioner(fx.Db), fx.Clock);

    /// <summary>
    /// July holds 30,000; August adds 22,480. Viewing July must show 30,000 —
    /// what the account held THEN, not the 52,480 it holds today (menunest-182).
    /// </summary>
    private static Guid SeedAccountWithTwoMonths(HandlerTestFixture fx)
    {
        var acc = BudgetAccount.Create(fx.Family.Id, "SCB", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, 30_000m, new DateOnly(2026, 7, 15), "Opening balance", fx.User.Id));
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, 22_480m, new DateOnly(2026, 8, 3), "Salary", fx.User.Id));
        return acc.Id;
    }

    [Fact]
    public async Task A_past_month_shows_the_balance_held_at_the_end_of_that_month()
    {
        using var fx = new HandlerTestFixture();
        SeedAccountWithTwoMonths(fx);
        await fx.Db.SaveChangesAsync();

        var july = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 7, Bkk), CancellationToken.None);

        july.Accounts.Single().Balance.Should().Be(30_000m);
    }

    [Fact]
    public async Task The_current_month_shows_every_transaction_to_date()
    {
        using var fx = new HandlerTestFixture();
        SeedAccountWithTwoMonths(fx);
        await fx.Db.SaveChangesAsync();

        var august = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 8, Bkk), CancellationToken.None);

        august.Accounts.Single().Balance.Should().Be(52_480m);
    }

    [Fact]
    public async Task A_month_before_the_first_transaction_shows_zero()
    {
        using var fx = new HandlerTestFixture();
        SeedAccountWithTwoMonths(fx);
        await fx.Db.SaveChangesAsync();

        var june = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 6, Bkk), CancellationToken.None);

        june.Accounts.Single().Balance.Should().Be(0m);
    }

    [Fact]
    public async Task A_transaction_on_the_last_day_of_the_month_is_included()
    {
        // Guards the boundary: the filter is Date < firstOfNextMonth, not Date < lastDay.
        using var fx = new HandlerTestFixture();
        var acc = BudgetAccount.Create(fx.Family.Id, "Cash", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, 500m, new DateOnly(2026, 7, 31), "Late", fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var july = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 7, Bkk), CancellationToken.None);

        july.Accounts.Single().Balance.Should().Be(500m);
    }

    [Fact]
    public async Task Ready_to_assign_uses_the_derived_balance_not_the_stored_one()
    {
        using var fx = new HandlerTestFixture();
        SeedAccountWithTwoMonths(fx);
        await fx.Db.SaveChangesAsync();

        var july = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 7, Bkk), CancellationToken.None);

        // No envelopes, so Ready to Assign is the whole derived account total.
        july.ReadyToAssign.Should().Be(30_000m);
    }

    [Fact]
    public async Task Another_familys_transactions_never_leak_in()
    {
        using var fx = new HandlerTestFixture();
        var acc = BudgetAccount.Create(fx.Family.Id, "Mine", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, 100m, new DateOnly(2026, 8, 1), null, fx.User.Id));
        // Foreign-family transaction pointing at MY account id — the only shape
        // that actually exercises the FamilyId predicate in the derived-balance
        // query. A random AccountId would land in the dictionary under a key
        // accountIds/accountRows never look up, passing regardless of the filter.
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            Guid.NewGuid(), acc.Id, null, 9_999m, new DateOnly(2026, 8, 1), null, fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var august = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 8, Bkk), CancellationToken.None);

        august.Accounts.Single().Balance.Should().Be(100m);
    }

    /// <summary>
    /// Guards two directions at once: (1) a CATEGORISED transaction must still
    /// reduce the account balance — the balance query loads ALL transactions,
    /// unlike the handler's `allTx`, which filters CategoryId != null for
    /// envelope activity; mutating the balance query to that same filter would
    /// leave every categorised expense uncounted. (2) the amount is NEGATIVE —
    /// BudgetTransaction's contract is signed (outflow is negative), and this
    /// is the first test in the suite to seed one.
    /// </summary>
    [Fact]
    public async Task A_categorised_negative_transaction_still_reduces_the_balance()
    {
        using var fx = new HandlerTestFixture();
        var acc = BudgetAccount.Create(fx.Family.Id, "Checking", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);

        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, null, 30_000m, new DateOnly(2026, 7, 15), "Opening balance", fx.User.Id));
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, cat.Id, -1_200m, new DateOnly(2026, 7, 20), "Groceries", fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var july = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 7, Bkk), CancellationToken.None);

        july.Accounts.Single().Balance.Should().Be(28_800m);
    }

    /// <summary>
    /// Two accounts, two different balances — verifies the grouped query
    /// attributes each account's own total, not the whole family's total to
    /// every account (e.g. summing balancesByAccount.Values instead of
    /// looking up by AccountId would pass every other test in this file,
    /// which all seed exactly one account).
    /// </summary>
    [Fact]
    public async Task Each_account_shows_its_own_balance_not_the_family_total()
    {
        using var fx = new HandlerTestFixture();
        var accA = BudgetAccount.Create(fx.Family.Id, "A", BudgetAccountType.Cash, 0m, 0);
        var accB = BudgetAccount.Create(fx.Family.Id, "B", BudgetAccountType.Cash, 0m, 1);
        fx.Db.BudgetAccounts.AddRange(accA, accB);
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, accA.Id, null, 1_000m, new DateOnly(2026, 7, 1), null, fx.User.Id));
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, accB.Id, null, 250m, new DateOnly(2026, 7, 1), null, fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var july = await Build(fx).Handle(new GetMonthlySummaryQuery(2026, 7, Bkk), CancellationToken.None);

        july.Accounts.Should().HaveCount(2);
        july.Accounts.Single(a => a.Name == "A").Balance.Should().Be(1_000m);
        july.Accounts.Single(a => a.Name == "B").Balance.Should().Be(250m);
    }
}
