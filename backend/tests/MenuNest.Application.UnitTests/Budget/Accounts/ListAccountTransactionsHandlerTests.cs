using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts.ListAccountTransactions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

public class ListAccountTransactionsHandlerTests
{
    private static ListAccountTransactionsHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object);

    private static async Task<BudgetAccount> SeedAccount(HandlerTestFixture fx, string name = "Cash")
    {
        var a = BudgetAccount.Create(fx.Family.Id, name, BudgetAccountType.Cash, 1000m, 0);
        fx.Db.BudgetAccounts.Add(a);
        await fx.Db.SaveChangesAsync();
        return a;
    }

    [Fact]
    public async Task Returns_account_summary_and_transactions_for_owned_account()
    {
        using var fx = new HandlerTestFixture();
        var acct = await SeedAccount(fx);
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acct.Id, null, 5000m,
            new DateOnly(2026, 5, 10), "salary", fx.User.Id));
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acct.Id, null, -200m,
            new DateOnly(2026, 5, 11), "lunch", fx.User.Id));
        // Out-of-month tx — should NOT be in MonthInflow/MonthOutflow.
        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acct.Id, null, -50m,
            new DateOnly(2026, 4, 30), "april expense", fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(
            new ListAccountTransactionsQuery(acct.Id, Year: 2026, Month: 5, Skip: 0, Take: 50),
            CancellationToken.None);

        result.Account.Id.Should().Be(acct.Id);
        result.Account.Name.Should().Be("Cash");
        result.Account.MonthInflow.Should().Be(5000m);
        result.Account.MonthOutflow.Should().Be(-200m);
        result.Items.Should().HaveCount(3);
        result.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Items_are_sorted_by_created_at_descending()
    {
        using var fx = new HandlerTestFixture();
        var acct = await SeedAccount(fx);

        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acct.Id, null, -10m, new DateOnly(2026, 5, 20),
            "older-create", fx.User.Id));
        await fx.Db.SaveChangesAsync();
        await Task.Delay(10);

        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acct.Id, null, -20m, new DateOnly(2026, 5, 10),
            "newer-create", fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(
            new ListAccountTransactionsQuery(acct.Id, 2026, 5, 0, 50),
            CancellationToken.None);

        result.Items.Select(t => t.Notes).Should()
            .ContainInOrder("newer-create", "older-create");
    }

    [Fact]
    public async Task Pagination_respects_skip_and_take_and_sets_hasmore()
    {
        using var fx = new HandlerTestFixture();
        var acct = await SeedAccount(fx);
        for (int i = 0; i < 5; i++)
        {
            fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
                fx.Family.Id, acct.Id, null, -1m,
                new DateOnly(2026, 5, 1).AddDays(i), $"tx-{i}", fx.User.Id));
            await fx.Db.SaveChangesAsync();
            await Task.Delay(5);
        }

        var page1 = await Build(fx).Handle(
            new ListAccountTransactionsQuery(acct.Id, 2026, 5, Skip: 0, Take: 2),
            CancellationToken.None);
        var page2 = await Build(fx).Handle(
            new ListAccountTransactionsQuery(acct.Id, 2026, 5, Skip: 2, Take: 2),
            CancellationToken.None);
        var page3 = await Build(fx).Handle(
            new ListAccountTransactionsQuery(acct.Id, 2026, 5, Skip: 4, Take: 2),
            CancellationToken.None);

        page1.Items.Should().HaveCount(2);
        page1.HasMore.Should().BeTrue();
        page2.Items.Should().HaveCount(2);
        page2.HasMore.Should().BeTrue();
        page3.Items.Should().HaveCount(1);
        page3.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Take_is_clamped_to_one_hundred()
    {
        using var fx = new HandlerTestFixture();
        var acct = await SeedAccount(fx);

        // Seed 150 transactions so the 100-row clamp is observable.
        var startDate = new DateOnly(2026, 5, 1);
        for (int i = 0; i < 150; i++)
        {
            fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
                fx.Family.Id, acct.Id, null, -1m,
                startDate, $"tx-{i}", fx.User.Id));
        }
        await fx.Db.SaveChangesAsync();

        var sut = Build(fx);
        var result = await sut.Handle(
            new ListAccountTransactionsQuery(acct.Id, 2026, 5, 0, Take: 10_000),
            CancellationToken.None);

        result.Items.Should().HaveCount(100);     // clamp applied
        result.HasMore.Should().BeTrue();          // 50 rows beyond the clamp
    }

    [Fact]
    public async Task Throws_when_account_does_not_belong_to_caller_family()
    {
        using var fx = new HandlerTestFixture();
        var foreign = BudgetAccount.Create(Guid.NewGuid(), "Foreign", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(foreign);
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var act = async () => await sut.Handle(
            new ListAccountTransactionsQuery(foreign.Id, 2026, 5, 0, 50),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Account not found.");
    }
}
