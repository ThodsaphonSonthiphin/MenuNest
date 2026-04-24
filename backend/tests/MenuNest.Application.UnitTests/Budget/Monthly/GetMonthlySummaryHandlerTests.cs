using System.Globalization;
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget;
using MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class GetMonthlySummaryHandlerTests
{
    /// <summary>
    /// A family with no groups, categories, or income should produce a
    /// completely empty summary (zeros + empty collections), not a null
    /// or an exception.
    /// </summary>
    [Fact]
    public async Task Empty_family_returns_zero_summary()
    {
        using var fx = new HandlerTestFixture();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

        result.Year.Should().Be(2026);
        result.Month.Should().Be(4);
        result.Income.Should().Be(0m);
        result.TotalAssigned.Should().Be(0m);
        result.TotalActivity.Should().Be(0m);
        result.Available.Should().Be(0m);
        result.LeftOverFromLastMonth.Should().Be(0m);
        result.ReadyToAssign.Should().Be(0m);
        result.Groups.Should().BeEmpty();
        result.Accounts.Should().BeEmpty();
    }

    /// <summary>
    /// Assigning 500 to a brand-new category with no spending yields
    /// Assigned=500, Activity=0, Available=500, LeftOver=0.
    /// </summary>
    [Fact]
    public async Task Single_category_assigned_no_spending_fills_envelope()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 4, 500m));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

        result.Groups.Should().HaveCount(1);
        var envelope = result.Groups[0].Categories.Single();
        envelope.Assigned.Should().Be(500m);
        envelope.Activity.Should().Be(0m);
        envelope.Available.Should().Be(500m);

        result.TotalAssigned.Should().Be(500m);
        result.TotalActivity.Should().Be(0m);
        result.Available.Should().Be(500m);
        result.LeftOverFromLastMonth.Should().Be(0m);
    }

    /// <summary>
    /// A −200 transaction against a 500-assigned category produces
    /// Activity=-200 (signed) and Available=300.
    /// </summary>
    [Fact]
    public async Task Spending_reduces_activity_and_available()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);

        var account = BudgetAccount.Create(
            fx.Family.Id, "Checking", BudgetAccountType.Cash, 10000m, 0);
        fx.Db.BudgetAccounts.Add(account);

        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 4, 500m));
        fx.Db.BudgetTransactions.Add(
            BudgetTransaction.Create(
                fx.Family.Id, account.Id, cat.Id, -200m,
                new DateOnly(2026, 4, 10), null, fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

        var envelope = result.Groups.Single().Categories.Single();
        envelope.Assigned.Should().Be(500m);
        envelope.Activity.Should().Be(-200m);
        envelope.Available.Should().Be(300m);

        result.TotalActivity.Should().Be(-200m);
        result.Available.Should().Be(300m);
    }

    /// <summary>
    /// When spending exceeds assigned, Available goes negative —
    /// the UI uses the sign to flag overspending.
    /// </summary>
    [Fact]
    public async Task Overspending_shows_negative_available()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);

        var account = BudgetAccount.Create(
            fx.Family.Id, "Checking", BudgetAccountType.Cash, 10000m, 0);
        fx.Db.BudgetAccounts.Add(account);

        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 4, 500m));
        fx.Db.BudgetTransactions.Add(
            BudgetTransaction.Create(
                fx.Family.Id, account.Id, cat.Id, -700m,
                new DateOnly(2026, 4, 10), null, fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

        var envelope = result.Groups.Single().Categories.Single();
        envelope.Assigned.Should().Be(500m);
        envelope.Activity.Should().Be(-700m);
        envelope.Available.Should().Be(-200m);
        (envelope.Available < 0).Should().BeTrue("overspending flag is derived from Available < 0");
    }

    /// <summary>
    /// Money assigned in March but unused rolls forward: with a −100
    /// transaction in April and 0 assigned in April, Available should
    /// show the March carry-in minus April activity.
    /// </summary>
    [Fact]
    public async Task Rollover_from_prior_month_carries_available_forward()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        fx.Db.BudgetCategories.Add(cat);

        var account = BudgetAccount.Create(
            fx.Family.Id, "Checking", BudgetAccountType.Cash, 10000m, 0);
        fx.Db.BudgetAccounts.Add(account);

        // March: 500 assigned, no activity → ends with 500 Available.
        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 3, 500m));
        // April: 0 assigned, -100 spent.
        fx.Db.BudgetTransactions.Add(
            BudgetTransaction.Create(
                fx.Family.Id, account.Id, cat.Id, -100m,
                new DateOnly(2026, 4, 5), null, fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

        var envelope = result.Groups.Single().Categories.Single();
        envelope.Assigned.Should().Be(0m);
        envelope.Activity.Should().Be(-100m);
        envelope.Available.Should().Be(400m, "500 rollover + 0 assigned + (-100) activity");

        // LeftOver = totalAvailable - totalAssigned - totalActivity
        //         = 400 - 0 - (-100) = 500 (matches March's ending Available).
        result.LeftOverFromLastMonth.Should().Be(500m);
    }

    /// <summary>
    /// With income=1000 and 500 assigned, ReadyToAssign = 1000 + 0 − 500 = 500.
    /// </summary>
    [Fact]
    public async Task Income_and_assignments_produce_ready_to_assign()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        fx.Db.BudgetCategories.Add(cat);

        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 4, 500m));
        fx.Db.MonthlyIncomes.Add(
            MonthlyIncome.Create(fx.Family.Id, 2026, 4, 1000m));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

        result.Income.Should().Be(1000m);
        result.TotalAssigned.Should().Be(500m);
        result.LeftOverFromLastMonth.Should().Be(0m);
        result.ReadyToAssign.Should().Be(500m);
    }

    /// <summary>
    /// A MonthlyAmount target of 1000 with 600 assigned produces
    /// fraction=0.6 and a hint naming the remaining amount and the
    /// configured day-of-month.
    /// </summary>
    [Fact]
    public async Task Target_progress_monthly_amount_partially_funded()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            using var fx = new HandlerTestFixture();

            var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
            fx.Db.BudgetCategoryGroups.Add(group);
            var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
            cat.SetMonthlyTarget(1000m, dayOfMonth: 1);
            fx.Db.BudgetCategories.Add(cat);

            fx.Db.MonthlyAssignments.Add(
                MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 4, 600m));
            await fx.Db.SaveChangesAsync();

            var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

            var result = await sut.Handle(
                new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

            var envelope = result.Groups.Single().Categories.Single();
            envelope.TargetProgressFraction.Should().Be(0.6m);
            envelope.TargetHint.Should().Be("฿400.00 more needed by the 1st");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// A ByDate target that's fully funded shows fraction=1 and
    /// suppresses the hint (nothing more needed).
    /// </summary>
    [Fact]
    public async Task Target_progress_by_date_fully_funded_suppresses_hint()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Savings", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Vacation", null, 0);
        cat.SetByDateTarget(500m, new DateOnly(2026, 12, 31));
        fx.Db.BudgetCategories.Add(cat);

        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 4, 500m));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

        var envelope = result.Groups.Single().Categories.Single();
        envelope.TargetProgressFraction.Should().Be(1m);
        envelope.TargetHint.Should().BeNull();
    }

    /// <summary>
    /// Hidden categories are dropped entirely from the response —
    /// they do not appear under groups[].categories nor do they
    /// contribute to the group or monthly totals.
    /// </summary>
    [Fact]
    public async Task Hidden_category_is_excluded_from_response_and_totals()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);

        var visible = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        var hidden = BudgetCategory.Create(fx.Family.Id, group.Id, "Old Gym", null, 1);
        hidden.Hide();
        fx.Db.BudgetCategories.AddRange(visible, hidden);

        fx.Db.MonthlyAssignments.AddRange(
            MonthlyAssignment.Create(fx.Family.Id, visible.Id, 2026, 4, 500m),
            MonthlyAssignment.Create(fx.Family.Id, hidden.Id, 2026, 4, 999m));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

        var grp = result.Groups.Single();
        grp.Categories.Should().HaveCount(1);
        grp.Categories.Single().Name.Should().Be("Rent");
        grp.TotalAssigned.Should().Be(500m, "hidden category must not inflate group totals");

        result.TotalAssigned.Should().Be(500m);
    }

    /// <summary>
    /// A second family's data must never leak into the caller's
    /// summary — the handler is filtered by the current user's
    /// familyId throughout.
    /// </summary>
    [Fact]
    public async Task Cross_family_data_is_isolated()
    {
        using var fx = new HandlerTestFixture();

        // Current-family data
        var myGroup = BudgetCategoryGroup.Create(fx.Family.Id, "Mine", 0);
        fx.Db.BudgetCategoryGroups.Add(myGroup);
        var myCat = BudgetCategory.Create(fx.Family.Id, myGroup.Id, "Rent", null, 0);
        fx.Db.BudgetCategories.Add(myCat);
        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(fx.Family.Id, myCat.Id, 2026, 4, 100m));
        fx.Db.BudgetAccounts.Add(BudgetAccount.Create(
            fx.Family.Id, "My Checking", BudgetAccountType.Cash, 1000m, 0));
        fx.Db.MonthlyIncomes.Add(MonthlyIncome.Create(fx.Family.Id, 2026, 4, 500m));

        // Foreign-family data
        var other = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(other);
        var otherGroup = BudgetCategoryGroup.Create(other.Id, "Foreign", 0);
        fx.Db.BudgetCategoryGroups.Add(otherGroup);
        var otherCat = BudgetCategory.Create(other.Id, otherGroup.Id, "Foreign Rent", null, 0);
        fx.Db.BudgetCategories.Add(otherCat);
        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(other.Id, otherCat.Id, 2026, 4, 9999m));
        fx.Db.BudgetAccounts.Add(BudgetAccount.Create(
            other.Id, "Foreign Checking", BudgetAccountType.Cash, 9999m, 0));
        fx.Db.MonthlyIncomes.Add(MonthlyIncome.Create(other.Id, 2026, 4, 9999m));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

        result.Groups.Should().HaveCount(1);
        result.Groups.Single().Name.Should().Be("Mine");
        result.Groups.Single().Categories.Single().Name.Should().Be("Rent");
        result.Accounts.Should().HaveCount(1);
        result.Accounts.Single().Name.Should().Be("My Checking");
        result.Income.Should().Be(500m);
        result.TotalAssigned.Should().Be(100m);
    }

    /// <summary>
    /// Multiple groups/categories should aggregate to consistent
    /// top-level totals and each group's nested totals.
    /// </summary>
    [Fact]
    public async Task Group_totals_aggregate_contained_categories()
    {
        using var fx = new HandlerTestFixture();

        var bills = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        var fun = BudgetCategoryGroup.Create(fx.Family.Id, "Fun", 1);
        fx.Db.BudgetCategoryGroups.AddRange(bills, fun);

        var rent = BudgetCategory.Create(fx.Family.Id, bills.Id, "Rent", null, 0);
        var utilities = BudgetCategory.Create(fx.Family.Id, bills.Id, "Utilities", null, 1);
        var games = BudgetCategory.Create(fx.Family.Id, fun.Id, "Games", null, 0);
        fx.Db.BudgetCategories.AddRange(rent, utilities, games);

        var account = BudgetAccount.Create(
            fx.Family.Id, "Checking", BudgetAccountType.Cash, 10000m, 0);
        fx.Db.BudgetAccounts.Add(account);

        fx.Db.MonthlyAssignments.AddRange(
            MonthlyAssignment.Create(fx.Family.Id, rent.Id, 2026, 4, 800m),
            MonthlyAssignment.Create(fx.Family.Id, utilities.Id, 2026, 4, 200m),
            MonthlyAssignment.Create(fx.Family.Id, games.Id, 2026, 4, 100m));
        fx.Db.BudgetTransactions.Add(
            BudgetTransaction.Create(
                fx.Family.Id, account.Id, games.Id, -40m,
                new DateOnly(2026, 4, 10), null, fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4), CancellationToken.None);

        result.Groups.Should().HaveCount(2);
        var billsDto = result.Groups.Single(g => g.Name == "Bills");
        var funDto = result.Groups.Single(g => g.Name == "Fun");

        billsDto.TotalAssigned.Should().Be(1000m);
        billsDto.TotalActivity.Should().Be(0m);
        billsDto.TotalAvailable.Should().Be(1000m);

        funDto.TotalAssigned.Should().Be(100m);
        funDto.TotalActivity.Should().Be(-40m);
        funDto.TotalAvailable.Should().Be(60m);

        result.TotalAssigned.Should().Be(1100m);
        result.TotalActivity.Should().Be(-40m);
        result.Available.Should().Be(1060m);
    }
}
