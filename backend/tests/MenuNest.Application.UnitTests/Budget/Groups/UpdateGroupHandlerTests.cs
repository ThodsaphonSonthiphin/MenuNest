using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Groups.UpdateGroup;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Groups;

public class UpdateGroupHandlerTests
{
    [Fact]
    public async Task Renames_and_reorders_existing_group()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Old Name", 1);
        fx.Db.BudgetCategoryGroups.Add(group);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateGroupHandler(fx.Db, fx.UserProvisioner.Object, new UpdateGroupValidator());

        var result = await sut.Handle(
            new UpdateGroupCommand(group.Id, "New Name", 9),
            CancellationToken.None);

        result.Name.Should().Be("New Name");
        result.SortOrder.Should().Be(9);

        var reloaded = fx.Db.BudgetCategoryGroups.Single(g => g.Id == group.Id);
        reloaded.Name.Should().Be("New Name");
        reloaded.SortOrder.Should().Be(9);
    }

    [Fact]
    public async Task Throws_DomainException_when_group_belongs_to_another_family()
    {
        using var fx = new HandlerTestFixture();

        var otherFamily = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(otherFamily);

        var foreign = BudgetCategoryGroup.Create(otherFamily.Id, "Foreign Group", 0);
        fx.Db.BudgetCategoryGroups.Add(foreign);
        await fx.Db.SaveChangesAsync();

        var sut = new UpdateGroupHandler(fx.Db, fx.UserProvisioner.Object, new UpdateGroupValidator());

        var act = async () => await sut.Handle(
            new UpdateGroupCommand(foreign.Id, "Hacked", 0),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Group not found*");
    }
}
