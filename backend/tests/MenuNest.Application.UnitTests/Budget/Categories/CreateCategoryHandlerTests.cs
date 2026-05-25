using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Categories.CreateCategory;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Categories;

public class CreateCategoryHandlerTests
{
    private static CreateCategoryHandler Build(HandlerTestFixture fx) =>
        new(fx.Db, fx.UserProvisioner.Object, new CreateCategoryValidator());

    private static async Task<BudgetCategoryGroup> SeedGroup(HandlerTestFixture fx, string name = "Bills", int sortOrder = 0)
    {
        var g = BudgetCategoryGroup.Create(fx.Family.Id, name, sortOrder);
        fx.Db.BudgetCategoryGroups.Add(g);
        await fx.Db.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task First_category_in_group_gets_sort_order_zero()
    {
        using var fx = new HandlerTestFixture();
        var group = await SeedGroup(fx);
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateCategoryCommand(group.Id, "Rent", Emoji: "🏠",
                TargetType: BudgetTargetType.None, TargetAmount: null,
                TargetDueDate: null, TargetDayOfMonth: null),
            CancellationToken.None);

        result.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task Sort_order_is_scoped_to_group()
    {
        using var fx = new HandlerTestFixture();
        var bills = await SeedGroup(fx, "Bills", 0);
        var fun = BudgetCategoryGroup.Create(fx.Family.Id, "Fun", 1);
        fx.Db.BudgetCategoryGroups.Add(fun);
        fx.Db.BudgetCategories.Add(BudgetCategory.Create(fx.Family.Id, bills.Id, "Rent", null, 5));
        fx.Db.BudgetCategories.Add(BudgetCategory.Create(fx.Family.Id, bills.Id, "Electric", null, 6));
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateCategoryCommand(fun.Id, "Dining", null,
                BudgetTargetType.None, null, null, null),
            CancellationToken.None);

        result.SortOrder.Should().Be(0); // Fun group is empty — starts from 0
    }

    [Fact]
    public async Task Subsequent_category_in_group_gets_max_plus_one()
    {
        using var fx = new HandlerTestFixture();
        var bills = await SeedGroup(fx);
        fx.Db.BudgetCategories.Add(BudgetCategory.Create(fx.Family.Id, bills.Id, "Rent", null, 5));
        fx.Db.BudgetCategories.Add(BudgetCategory.Create(fx.Family.Id, bills.Id, "Electric", null, 6));
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateCategoryCommand(bills.Id, "Water", null,
                BudgetTargetType.None, null, null, null),
            CancellationToken.None);

        result.SortOrder.Should().Be(7);
    }

    [Fact]
    public async Task Max_is_scoped_to_calling_family_only()
    {
        using var fx = new HandlerTestFixture();
        var ownGroup = await SeedGroup(fx);
        // Seed another family with a high-sortOrder category under a different group.
        var otherFamilyId = Guid.NewGuid();
        var otherGroup = BudgetCategoryGroup.Create(otherFamilyId, "OtherGroup", 0);
        fx.Db.BudgetCategoryGroups.Add(otherGroup);
        fx.Db.BudgetCategories.Add(BudgetCategory.Create(otherFamilyId, otherGroup.Id, "OtherCat", null, 99));
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var result = await sut.Handle(
            new CreateCategoryCommand(ownGroup.Id, "Mine", null,
                BudgetTargetType.None, null, null, null),
            CancellationToken.None);

        result.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task Throws_when_group_does_not_belong_to_caller_family()
    {
        using var fx = new HandlerTestFixture();
        var otherFamilyId = Guid.NewGuid();
        var foreignGroup = BudgetCategoryGroup.Create(otherFamilyId, "Foreign", 0);
        fx.Db.BudgetCategoryGroups.Add(foreignGroup);
        await fx.Db.SaveChangesAsync();
        var sut = Build(fx);

        var act = async () => await sut.Handle(
            new CreateCategoryCommand(foreignGroup.Id, "Sneaky", null,
                BudgetTargetType.None, null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Group not found.");
    }

    [Fact]
    public async Task Rejects_blank_name()
    {
        using var fx = new HandlerTestFixture();
        var group = await SeedGroup(fx);
        var sut = Build(fx);

        var act = async () => await sut.Handle(
            new CreateCategoryCommand(group.Id, "  ", null,
                BudgetTargetType.None, null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Rejects_name_longer_than_120_characters()
    {
        using var fx = new HandlerTestFixture();
        var group = await SeedGroup(fx);
        var sut = Build(fx);

        var act = async () => await sut.Handle(
            new CreateCategoryCommand(group.Id, new string('a', 121), null,
                BudgetTargetType.None, null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
