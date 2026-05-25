using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Groups.CreateGroup;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Budget.Groups;

public class CreateGroupHandlerTests
{
    private static CreateGroupHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new CreateGroupValidator());

    [Fact]
    public async Task First_group_in_family_gets_sort_order_zero()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var result = await sut.Handle(new CreateGroupCommand("Bills"), CancellationToken.None);

        result.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task Subsequent_group_gets_max_plus_one()
    {
        using var fx = new HandlerTestFixture();
        fx.Db.BudgetCategoryGroups.Add(BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0));
        fx.Db.BudgetCategoryGroups.Add(BudgetCategoryGroup.Create(fx.Family.Id, "Fun", 7));
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var result = await sut.Handle(new CreateGroupCommand("Savings"), CancellationToken.None);

        result.SortOrder.Should().Be(8);
    }

    [Fact]
    public async Task Rejects_blank_name()
    {
        using var fx = new HandlerTestFixture();
        var sut = Build(fx);

        var act = async () => await sut.Handle(new CreateGroupCommand("  "), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
