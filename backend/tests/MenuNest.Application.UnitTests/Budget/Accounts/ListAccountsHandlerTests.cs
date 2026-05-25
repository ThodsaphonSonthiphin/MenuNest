using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts.ListAccounts;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

public class ListAccountsHandlerTests
{
    private static ListAccountsHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object);

    [Fact]
    public async Task Sorts_by_created_at_descending()
    {
        using var fx = new HandlerTestFixture();
        var first = BudgetAccount.Create(fx.Family.Id, "Older", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(first);
        await fx.Db.SaveChangesAsync();
        await Task.Delay(10);

        var middle = BudgetAccount.Create(fx.Family.Id, "Middle", BudgetAccountType.Credit, 0m, 0);
        fx.Db.BudgetAccounts.Add(middle);
        await fx.Db.SaveChangesAsync();
        await Task.Delay(10);

        var newest = BudgetAccount.Create(fx.Family.Id, "Newest", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(newest);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(new ListAccountsQuery(), CancellationToken.None);

        result.Select(a => a.Name).Should().ContainInOrder("Newest", "Middle", "Older");
    }

    [Fact]
    public async Task Excludes_closed_accounts()
    {
        using var fx = new HandlerTestFixture();
        var open = BudgetAccount.Create(fx.Family.Id, "Open", BudgetAccountType.Cash, 0m, 0);
        var closed = BudgetAccount.Create(fx.Family.Id, "Closed", BudgetAccountType.Cash, 0m, 0);
        closed.Close();
        fx.Db.BudgetAccounts.AddRange(open, closed);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(new ListAccountsQuery(), CancellationToken.None);

        result.Select(a => a.Name).Should().ContainSingle().Which.Should().Be("Open");
    }

    [Fact]
    public async Task Only_returns_callers_family_accounts()
    {
        using var fx = new HandlerTestFixture();
        var mine = BudgetAccount.Create(fx.Family.Id, "Mine", BudgetAccountType.Cash, 0m, 0);
        var theirs = BudgetAccount.Create(Guid.NewGuid(), "Theirs", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.AddRange(mine, theirs);
        await fx.Db.SaveChangesAsync();

        var result = await Build(fx).Handle(new ListAccountsQuery(), CancellationToken.None);

        result.Select(a => a.Name).Should().ContainSingle().Which.Should().Be("Mine");
    }
}
