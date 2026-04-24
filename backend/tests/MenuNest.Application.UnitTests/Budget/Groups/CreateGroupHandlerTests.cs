using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Groups.CreateGroup;

namespace MenuNest.Application.UnitTests.Budget.Groups;

public class CreateGroupHandlerTests
{
    [Fact]
    public async Task Creates_group_with_provided_values_scoped_to_current_family()
    {
        using var fx = new HandlerTestFixture();
        var sut = new CreateGroupHandler(fx.Db, fx.UserProvisioner.Object, new CreateGroupValidator());

        var result = await sut.Handle(new CreateGroupCommand("Bills", 4), CancellationToken.None);

        result.Name.Should().Be("Bills");
        result.SortOrder.Should().Be(4);
        result.IsHidden.Should().BeFalse();

        var persisted = fx.Db.BudgetCategoryGroups.Single(g => g.Id == result.Id);
        persisted.FamilyId.Should().Be(fx.Family.Id);
        persisted.Name.Should().Be("Bills");
        persisted.SortOrder.Should().Be(4);
    }

    [Fact]
    public async Task Throws_ValidationException_when_name_is_empty()
    {
        using var fx = new HandlerTestFixture();
        var sut = new CreateGroupHandler(fx.Db, fx.UserProvisioner.Object, new CreateGroupValidator());

        var act = async () => await sut.Handle(
            new CreateGroupCommand("", 0), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Throws_ValidationException_when_name_exceeds_120_characters()
    {
        using var fx = new HandlerTestFixture();
        var sut = new CreateGroupHandler(fx.Db, fx.UserProvisioner.Object, new CreateGroupValidator());

        var longName = new string('a', 121);
        var act = async () => await sut.Handle(
            new CreateGroupCommand(longName, 0), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
