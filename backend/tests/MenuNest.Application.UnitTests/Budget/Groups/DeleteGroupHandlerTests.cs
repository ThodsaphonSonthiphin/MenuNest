using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Groups.DeleteGroup;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Groups;

public class DeleteGroupHandlerTests
{
    [Fact]
    public async Task Deletes_group_with_no_categories()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        await fx.Db.SaveChangesAsync();

        var sut = new DeleteGroupHandler(fx.Db, fx.UserProvisioner.Object);

        await sut.Handle(new DeleteGroupCommand(group.Id), CancellationToken.None);

        fx.Db.BudgetCategoryGroups.Any(g => g.Id == group.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Throws_DomainException_when_group_has_categories()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);

        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", emoji: null, sortOrder: 0);
        fx.Db.BudgetCategories.Add(cat);
        await fx.Db.SaveChangesAsync();

        var sut = new DeleteGroupHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(new DeleteGroupCommand(group.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        fx.Db.BudgetCategoryGroups.Any(g => g.Id == group.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Throws_DomainException_when_group_belongs_to_another_family()
    {
        using var fx = new HandlerTestFixture();

        var otherFamily = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(otherFamily);
        var foreign = BudgetCategoryGroup.Create(otherFamily.Id, "Foreign", 0);
        fx.Db.BudgetCategoryGroups.Add(foreign);
        await fx.Db.SaveChangesAsync();

        var sut = new DeleteGroupHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(new DeleteGroupCommand(foreign.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Group not found*");
    }
}
