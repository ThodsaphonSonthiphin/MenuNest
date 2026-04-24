using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Transactions.ListTransactions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Transactions;

public class ListTransactionsHandlerTests
{
    [Fact]
    public async Task Returns_transactions_for_family_in_year_month_ordered_by_date_desc()
    {
        using var fx = new HandlerTestFixture();

        var acc = BudgetAccount.Create(fx.Family.Id, "Checking", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);

        var txApr01 = BudgetTransaction.Create(fx.Family.Id, acc.Id, null, -10m,
            new DateOnly(2026, 4, 1), null, fx.User.Id);
        var txApr15 = BudgetTransaction.Create(fx.Family.Id, acc.Id, null, -20m,
            new DateOnly(2026, 4, 15), null, fx.User.Id);
        var txApr30 = BudgetTransaction.Create(fx.Family.Id, acc.Id, null, -30m,
            new DateOnly(2026, 4, 30), null, fx.User.Id);
        // Out-of-month — excluded.
        var txMar31 = BudgetTransaction.Create(fx.Family.Id, acc.Id, null, -99m,
            new DateOnly(2026, 3, 31), null, fx.User.Id);
        var txMay01 = BudgetTransaction.Create(fx.Family.Id, acc.Id, null, -99m,
            new DateOnly(2026, 5, 1), null, fx.User.Id);

        fx.Db.BudgetTransactions.AddRange(txApr01, txApr15, txApr30, txMar31, txMay01);
        await fx.Db.SaveChangesAsync();

        var sut = new ListTransactionsHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new ListTransactionsQuery(2026, 4, CategoryId: null), CancellationToken.None);

        result.Should().HaveCount(3);
        result.Select(t => t.Date).Should().ContainInOrder(
            new DateOnly(2026, 4, 30),
            new DateOnly(2026, 4, 15),
            new DateOnly(2026, 4, 1));
        result.Should().OnlyContain(t => t.AccountName == "Checking");
    }

    [Fact]
    public async Task Filters_by_categoryId_when_provided_and_returns_all_when_null()
    {
        using var fx = new HandlerTestFixture();

        var acc = BudgetAccount.Create(fx.Family.Id, "Checking", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var catGroceries = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", emoji: "🥕", sortOrder: 0);
        var catFuel = BudgetCategory.Create(fx.Family.Id, group.Id, "Fuel", emoji: null, sortOrder: 1);
        fx.Db.BudgetCategories.AddRange(catGroceries, catFuel);

        var date = new DateOnly(2026, 4, 10);
        var txGroceries = BudgetTransaction.Create(fx.Family.Id, acc.Id, catGroceries.Id, -50m, date, null, fx.User.Id);
        var txFuel = BudgetTransaction.Create(fx.Family.Id, acc.Id, catFuel.Id, -40m, date, null, fx.User.Id);
        var txUncat = BudgetTransaction.Create(fx.Family.Id, acc.Id, null, 1000m, date, null, fx.User.Id);
        fx.Db.BudgetTransactions.AddRange(txGroceries, txFuel, txUncat);
        await fx.Db.SaveChangesAsync();

        var sut = new ListTransactionsHandler(fx.Db, fx.UserProvisioner.Object);

        var filtered = await sut.Handle(new ListTransactionsQuery(2026, 4, catGroceries.Id), CancellationToken.None);
        filtered.Should().ContainSingle();
        filtered[0].CategoryId.Should().Be(catGroceries.Id);
        filtered[0].CategoryName.Should().Be("Groceries");
        filtered[0].CategoryEmoji.Should().Be("🥕");

        var all = await sut.Handle(new ListTransactionsQuery(2026, 4, CategoryId: null), CancellationToken.None);
        all.Should().HaveCount(3);
    }

    [Fact]
    public async Task Excludes_transactions_from_other_families()
    {
        using var fx = new HandlerTestFixture();

        var acc = BudgetAccount.Create(fx.Family.Id, "Checking", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);

        var otherFamily = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(otherFamily);
        var otherAcc = BudgetAccount.Create(otherFamily.Id, "Other Checking", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(otherAcc);

        var date = new DateOnly(2026, 4, 10);
        var mine = BudgetTransaction.Create(fx.Family.Id, acc.Id, null, -10m, date, null, fx.User.Id);
        var theirs = BudgetTransaction.Create(otherFamily.Id, otherAcc.Id, null, -999m, date, null, fx.User.Id);
        fx.Db.BudgetTransactions.AddRange(mine, theirs);
        await fx.Db.SaveChangesAsync();

        var sut = new ListTransactionsHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new ListTransactionsQuery(2026, 4, CategoryId: null), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(mine.Id);
    }
}
