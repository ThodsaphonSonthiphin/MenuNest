using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Monthly.SetMonthlyIncome;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class SetMonthlyIncomeHandlerTests
{
    [Fact]
    public async Task Creates_new_income_row_on_first_call()
    {
        using var fx = new HandlerTestFixture();

        var sut = new SetMonthlyIncomeHandler(
            fx.Db, fx.UserProvisioner.Object, new SetMonthlyIncomeValidator());

        await sut.Handle(
            new SetMonthlyIncomeCommand(Year: 2026, Month: 4, Amount: 50000m),
            CancellationToken.None);

        var persisted = fx.Db.MonthlyIncomes.Single();
        persisted.FamilyId.Should().Be(fx.Family.Id);
        persisted.Year.Should().Be(2026);
        persisted.Month.Should().Be(4);
        persisted.Amount.Should().Be(50000m);
    }

    [Fact]
    public async Task Updates_existing_income_row_on_second_call_for_same_family_year_month()
    {
        using var fx = new HandlerTestFixture();

        var sut = new SetMonthlyIncomeHandler(
            fx.Db, fx.UserProvisioner.Object, new SetMonthlyIncomeValidator());

        await sut.Handle(
            new SetMonthlyIncomeCommand(Year: 2026, Month: 4, Amount: 50000m),
            CancellationToken.None);
        await sut.Handle(
            new SetMonthlyIncomeCommand(Year: 2026, Month: 4, Amount: 75000m),
            CancellationToken.None);

        fx.Db.MonthlyIncomes.Should().HaveCount(1);
        fx.Db.MonthlyIncomes.Single().Amount.Should().Be(75000m);
    }

    [Fact]
    public async Task Throws_ValidationException_when_amount_is_negative()
    {
        using var fx = new HandlerTestFixture();

        var sut = new SetMonthlyIncomeHandler(
            fx.Db, fx.UserProvisioner.Object, new SetMonthlyIncomeValidator());

        var act = async () => await sut.Handle(
            new SetMonthlyIncomeCommand(Year: 2026, Month: 4, Amount: -1m),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
