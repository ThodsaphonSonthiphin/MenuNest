using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget.Groups.ListGroups;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Budget.Groups;

public class ListGroupsHandlerTests
{
    [Fact]
    public async Task Returns_only_groups_belonging_to_current_family_ordered_by_sortorder_then_name()
    {
        using var fx = new HandlerTestFixture();

        // Another family's group must be excluded.
        var otherFamily = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(otherFamily);
        fx.Db.BudgetCategoryGroups.Add(BudgetCategoryGroup.Create(otherFamily.Id, "Foreign Group", 0));

        // Seed with out-of-order sort orders and names to exercise ordering.
        var bills = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 2);
        var fun   = BudgetCategoryGroup.Create(fx.Family.Id, "Fun", 5);
        var savingsA = BudgetCategoryGroup.Create(fx.Family.Id, "Automatic Savings", 1);
        var savingsB = BudgetCategoryGroup.Create(fx.Family.Id, "Manual Savings", 1);
        fx.Db.BudgetCategoryGroups.AddRange(bills, fun, savingsA, savingsB);
        await fx.Db.SaveChangesAsync();

        var sut = new ListGroupsHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new ListGroupsQuery(), CancellationToken.None);

        result.Select(g => g.Name).Should().ContainInOrder(
            "Automatic Savings", // sort 1, 'A' < 'M'
            "Manual Savings",    // sort 1
            "Bills",             // sort 2
            "Fun"                // sort 5
        );
        result.Should().HaveCount(4);
        result.Should().NotContain(g => g.Name == "Foreign Group");
    }

    [Fact]
    public async Task Includes_hidden_groups_alongside_visible_ones()
    {
        // Design choice: the envelope page is responsible for filtering hidden
        // groups in the UI, so the admin list returns everything.
        using var fx = new HandlerTestFixture();

        var visible = BudgetCategoryGroup.Create(fx.Family.Id, "Visible", 0);
        var hidden  = BudgetCategoryGroup.Create(fx.Family.Id, "Hidden", 1);
        hidden.Hide();
        fx.Db.BudgetCategoryGroups.AddRange(visible, hidden);
        await fx.Db.SaveChangesAsync();

        var sut = new ListGroupsHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(new ListGroupsQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Single(g => g.Name == "Hidden").IsHidden.Should().BeTrue();
        result.Single(g => g.Name == "Visible").IsHidden.Should().BeFalse();
    }
}
