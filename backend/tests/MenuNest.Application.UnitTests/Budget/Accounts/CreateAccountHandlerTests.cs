using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Accounts;

public class CreateAccountHandlerTests
{
    [Fact]
    public async Task Creates_account_with_provided_values_scoped_to_current_family()
    {
        using var fx = new HandlerTestFixture();
        var sut = new CreateAccountHandler(fx.Db, fx.UserProvisioner.Object, new CreateAccountValidator());

        var result = await sut.Handle(
            new CreateAccountCommand("Checking", BudgetAccountType.Cash, 1500.50m, 3),
            CancellationToken.None);

        result.Name.Should().Be("Checking");
        result.Type.Should().Be(BudgetAccountType.Cash);
        result.Balance.Should().Be(1500.50m);
        result.SortOrder.Should().Be(3);
        result.IsClosed.Should().BeFalse();

        var persisted = fx.Db.BudgetAccounts.Single(a => a.Id == result.Id);
        persisted.FamilyId.Should().Be(fx.Family.Id);
        persisted.Name.Should().Be("Checking");
        persisted.Balance.Should().Be(1500.50m);
    }

    [Fact]
    public async Task Throws_ValidationException_when_name_is_empty()
    {
        using var fx = new HandlerTestFixture();
        var sut = new CreateAccountHandler(fx.Db, fx.UserProvisioner.Object, new CreateAccountValidator());

        var act = async () => await sut.Handle(
            new CreateAccountCommand("", BudgetAccountType.Cash, 0m, 0),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Throws_ValidationException_when_name_exceeds_120_characters()
    {
        using var fx = new HandlerTestFixture();
        var sut = new CreateAccountHandler(fx.Db, fx.UserProvisioner.Object, new CreateAccountValidator());

        var longName = new string('a', 121);
        var act = async () => await sut.Handle(
            new CreateAccountCommand(longName, BudgetAccountType.Cash, 0m, 0),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
