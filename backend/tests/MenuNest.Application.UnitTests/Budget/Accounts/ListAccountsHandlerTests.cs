using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts.ListAccounts;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

public class ListAccountsHandlerTests
{
    [Fact]
    public async Task Returns_accounts_for_current_family_ordered_by_isclosed_type_sortorder_name()
    {
        using var fx = new HandlerTestFixture();

        // Another family's account — must be excluded.
        var otherFamily = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(otherFamily);
        fx.Db.BudgetAccounts.Add(
            BudgetAccount.Create(otherFamily.Id, "Other Cash", BudgetAccountType.Cash, 0m, 0));

        // Mix of accounts to exercise ordering:
        //   IsClosed asc -> open before closed
        //   Type asc     -> Cash(1) < Credit(2) < Loan(3)
        //   SortOrder asc
        //   Name asc
        var closedCash   = BudgetAccount.Create(fx.Family.Id, "Old Wallet",  BudgetAccountType.Cash,   0m, 0);
        closedCash.Close();
        var openLoan     = BudgetAccount.Create(fx.Family.Id, "Car Loan",    BudgetAccountType.Loan,   0m, 0);
        var openCreditA  = BudgetAccount.Create(fx.Family.Id, "Visa",        BudgetAccountType.Credit, 0m, 5);
        var openCreditB  = BudgetAccount.Create(fx.Family.Id, "Amex",        BudgetAccountType.Credit, 0m, 5);
        var openCashHigh = BudgetAccount.Create(fx.Family.Id, "Savings",     BudgetAccountType.Cash,   0m, 10);
        var openCashLow  = BudgetAccount.Create(fx.Family.Id, "Checking",    BudgetAccountType.Cash,   0m, 1);

        fx.Db.BudgetAccounts.AddRange(closedCash, openLoan, openCreditA, openCreditB, openCashHigh, openCashLow);
        await fx.Db.SaveChangesAsync();

        var sut = new ListAccountsHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new ListAccountsQuery(), CancellationToken.None);

        result.Select(a => a.Name).Should().ContainInOrder(
            "Checking",   // open, Cash, sort 1
            "Savings",    // open, Cash, sort 10
            "Amex",       // open, Credit, sort 5, name 'A' < 'V'
            "Visa",       // open, Credit, sort 5
            "Car Loan",   // open, Loan
            "Old Wallet"  // closed
        );
        result.Should().HaveCount(6);
        result.Should().NotContain(a => a.Name == "Other Cash");
    }
}
