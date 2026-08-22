using System.Globalization;
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Budget;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class GetMonthlySummaryHandlerTests
{
    // The app's one real time zone (menunest-189) — every user is in Thailand.
    private const string Bkk = "Asia/Bangkok";

    /// <summary>
    /// A family with no groups, categories, or income should produce a
    /// completely empty summary (zeros + empty collections), not a null
    /// or an exception.
    /// </summary>
    [Fact]
    public async Task Empty_family_returns_zero_summary()
    {
        using var fx = new HandlerTestFixture();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4, Bkk), CancellationToken.None);

        result.Year.Should().Be(2026);
        result.Month.Should().Be(4);
        result.Income.Should().Be(0m);
        result.TotalAssigned.Should().Be(0m);
        result.TotalActivity.Should().Be(0m);
        result.Available.Should().Be(0m);
        result.ReadyToAssign.Should().Be(0m);
        result.Groups.Should().BeEmpty();
        result.Accounts.Should().BeEmpty();
    }

    /// <summary>
    /// Assigning 500 to a brand-new category with no spending yields
    /// Assigned=500, Activity=0, Available=500.
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

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4, Bkk), CancellationToken.None);

        result.Groups.Should().HaveCount(1);
        var envelope = result.Groups[0].Categories.Single();
        envelope.Assigned.Should().Be(500m);
        envelope.Activity.Should().Be(0m);
        envelope.Available.Should().Be(500m);

        result.TotalAssigned.Should().Be(500m);
        result.TotalActivity.Should().Be(0m);
        result.Available.Should().Be(500m);
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

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4, Bkk), CancellationToken.None);

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

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4, Bkk), CancellationToken.None);

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

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4, Bkk), CancellationToken.None);

        var envelope = result.Groups.Single().Categories.Single();
        envelope.Assigned.Should().Be(0m);
        envelope.Activity.Should().Be(-100m);
        envelope.Available.Should().Be(400m, "500 rollover + 0 assigned + (-100) activity");
    }

    /// <summary>
    /// RTA = sum(accounts) − sum(envelope.available across all cats).
    /// 1000 in an account + 500 assigned to a single category produces
    /// envelope.available 500 → ReadyToAssign 1000 − 500 = 500.
    /// </summary>
    [Fact]
    public async Task Account_balance_minus_envelope_available_produces_ready_to_assign()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        fx.Db.BudgetCategories.Add(cat);

        var account = BudgetAccount.Create(
            fx.Family.Id, "Checking", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(account);
        // Balance is now derived from transactions, not the stored opening
        // balance — seed a real uncategorized inflow instead.
        fx.Db.BudgetTransactions.Add(
            BudgetTransaction.Create(
                fx.Family.Id, account.Id, null, 1000m,
                new DateOnly(2026, 4, 1), null, fx.User.Id));
        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 4, 500m));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4, Bkk), CancellationToken.None);

        result.TotalAssigned.Should().Be(500m);
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

            var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

            var result = await sut.Handle(
                new GetMonthlySummaryQuery(2026, 4, Bkk), CancellationToken.None);

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

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4, Bkk), CancellationToken.None);

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

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4, Bkk), CancellationToken.None);

        var grp = result.Groups.Single();
        grp.Categories.Should().HaveCount(1);
        grp.Categories.Single().Name.Should().Be("Rent");
        grp.TotalAssigned.Should().Be(500m, "hidden category must not inflate group totals");

        result.TotalAssigned.Should().Be(500m);
    }

    /// <summary>
    /// Hidden categories are hidden from the response, but their
    /// envelope.available is still subtracted from RTA. Without this,
    /// hiding a funded category would silently inflate the RTA.
    /// </summary>
    [Fact]
    public async Task Hidden_category_is_subtracted_from_rta_even_though_hidden_from_response()
    {
        using var fx = new HandlerTestFixture();

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Bills", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var visible = BudgetCategory.Create(fx.Family.Id, group.Id, "Rent", null, 0);
        var hidden  = BudgetCategory.Create(fx.Family.Id, group.Id, "Old Gym", null, 1);
        hidden.Hide();
        fx.Db.BudgetCategories.AddRange(visible, hidden);

        var account = BudgetAccount.Create(
            fx.Family.Id, "Checking", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(account);
        // Balance is now derived from transactions, not the stored opening
        // balance — seed a real uncategorized inflow instead.
        fx.Db.BudgetTransactions.Add(
            BudgetTransaction.Create(
                fx.Family.Id, account.Id, null, 1000m,
                new DateOnly(2026, 4, 1), null, fx.User.Id));
        fx.Db.MonthlyAssignments.AddRange(
            MonthlyAssignment.Create(fx.Family.Id, visible.Id, 2026, 4, 300m),
            MonthlyAssignment.Create(fx.Family.Id, hidden.Id,  2026, 4, 300m));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4, Bkk), CancellationToken.None);

        // Response excludes the hidden cat (existing behavior).
        result.Groups.Single().Categories.Should().HaveCount(1);
        result.TotalAssigned.Should().Be(300m, "visible-only sum drives the UI total");

        // But hidden cat IS subtracted from RTA.
        result.ReadyToAssign.Should().Be(400m, "1000 − (300 visible + 300 hidden) = 400");
    }

    /// <summary>
    /// Income now means "sum of positive inflow transactions this month".
    /// Seed two positive inflows and one negative uncategorized outflow;
    /// assert only the positive ones contribute.
    /// </summary>
    [Fact]
    public async Task Income_field_is_sum_of_positive_inflows_this_month()
    {
        using var fx = new HandlerTestFixture();

        var account = BudgetAccount.Create(
            fx.Family.Id, "Checking", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(account);

        fx.Db.BudgetTransactions.AddRange(
            BudgetTransaction.Create(fx.Family.Id, account.Id, null,  200m,
                new DateOnly(2026, 4, 1), null, fx.User.Id),
            BudgetTransaction.Create(fx.Family.Id, account.Id, null,  300m,
                new DateOnly(2026, 4, 15), null, fx.User.Id),
            BudgetTransaction.Create(fx.Family.Id, account.Id, null, -50m,
                new DateOnly(2026, 4, 20), null, fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4, Bkk), CancellationToken.None);

        result.Income.Should().Be(500m, "only positive uncategorized inflows count toward Income");
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
        var myAccount = BudgetAccount.Create(fx.Family.Id, "My Checking", BudgetAccountType.Cash, 1000m, 0);
        fx.Db.BudgetAccounts.Add(myAccount);

        // Foreign-family data
        var other = Family.CreateNew("Other Family", fx.User.Id);
        fx.Db.Families.Add(other);
        var otherGroup = BudgetCategoryGroup.Create(other.Id, "Foreign", 0);
        fx.Db.BudgetCategoryGroups.Add(otherGroup);
        var otherCat = BudgetCategory.Create(other.Id, otherGroup.Id, "Foreign Rent", null, 0);
        fx.Db.BudgetCategories.Add(otherCat);
        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(other.Id, otherCat.Id, 2026, 4, 9999m));
        var otherAccount = BudgetAccount.Create(other.Id, "Foreign Checking", BudgetAccountType.Cash, 9999m, 0);
        fx.Db.BudgetAccounts.Add(otherAccount);

        // One positive inflow per family
        fx.Db.BudgetTransactions.AddRange(
            BudgetTransaction.Create(fx.Family.Id, myAccount.Id, null, 500m,
                new DateOnly(2026, 4, 1), null, fx.User.Id),
            BudgetTransaction.Create(other.Id, otherAccount.Id, null, 9999m,
                new DateOnly(2026, 4, 1), null, fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4, Bkk), CancellationToken.None);

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

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 4, Bkk), CancellationToken.None);

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

    // ── Daily allowance card (menunest-185/189) ─────────────────────────────

    /// <summary>
    /// The viewer's "today" for these tests: converts the fixture's
    /// deterministic <see cref="FixedClock"/> through Bangkok, exactly as the
    /// handler now does internally — never real wall-clock time.
    /// </summary>
    private static DateOnly TodayIn(HandlerTestFixture fx) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            fx.Clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(Bkk)));

    [Fact]
    public async Task Allowance_card_is_null_when_the_requested_month_is_not_the_real_current_month()
    {
        using var fx = new HandlerTestFixture();
        var today = TodayIn(fx);
        var farPast = new DateOnly(today.Year - 5, 1, 1);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        cat.MarkEveryday(true);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(fx.Family.Id, cat.Id, farPast.Year, farPast.Month, 5000m));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(farPast.Year, farPast.Month, Bkk), CancellationToken.None);

        result.DailyAllowance.Should().BeNull();
        fx.Db.DailyAllowances.Should().BeEmpty("a non-current month must never trigger a freeze");
    }

    [Fact]
    public async Task Allowance_card_shows_HasMarks_false_when_nothing_is_marked_everyday()
    {
        using var fx = new HandlerTestFixture();
        var today = TodayIn(fx);

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(today.Year, today.Month, Bkk), CancellationToken.None);

        result.DailyAllowance.Should().Be(new DailyAllowanceDto(0m, today, 0m, HasMarks: false));
        fx.Db.DailyAllowances.Should().BeEmpty("nothing marked means no row is ever written");
    }

    [Fact]
    public async Task Allowance_card_lazily_freezes_on_first_read_of_the_current_month()
    {
        using var fx = new HandlerTestFixture();
        var today = TodayIn(fx);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        cat.MarkEveryday(true);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(fx.Family.Id, cat.Id, today.Year, today.Month, 6000m));
        await fx.Db.SaveChangesAsync();
        fx.Db.DailyAllowances.Should().BeEmpty("nothing frozen yet");

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(today.Year, today.Month, Bkk), CancellationToken.None);

        result.DailyAllowance.Should().NotBeNull();
        result.DailyAllowance!.HasMarks.Should().BeTrue();
        result.DailyAllowance.FrozenOn.Should().Be(today);
        fx.Db.DailyAllowances.Should().ContainSingle("reading the summary must persist the lazily-created freeze");
    }

    [Fact]
    public async Task Allowance_card_lazily_rolls_a_stale_row_over_to_the_current_month()
    {
        using var fx = new HandlerTestFixture();
        var today = TodayIn(fx);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        cat.MarkEveryday(true);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(fx.Family.Id, cat.Id, today.Year, today.Month, 3000m));
        await fx.Db.SaveChangesAsync();

        // A row frozen for last month is stale for this month's card.
        var lastMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        fx.Db.DailyAllowances.Add(DailyAllowance.Freeze(fx.Family.Id, 9999m, lastMonth));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(today.Year, today.Month, Bkk), CancellationToken.None);

        result.DailyAllowance.Should().NotBeNull();
        result.DailyAllowance!.FrozenOn.Should().Be(today, "a stale row must be refrozen, not served as-is");

        var row = fx.Db.DailyAllowances.Single();
        row.IsForMonth(today.Year, today.Month).Should().BeTrue();
        row.FrozenPot.Should().Be(3000m, "the refreeze must use THIS month's pot, not the stale row's 9999");
    }

    // Rollover must key off the MONTH, not the exact day. A row frozen earlier
    // THIS month (a real Budgeting event a few days ago) is still valid today —
    // reading the summary is not itself a Budgeting event and must not reset
    // FrozenOn just because it isn't literally today. Guards against the
    // specific wrong guard `row.FrozenOn != today` (vs. `!row.IsForMonth(...)`),
    // which would re-freeze on every single read and permanently zero the Pace
    // line. Calls Handle twice to also pin idempotency across repeated reads.
    //
    // Pinned to a FIXED clock on the 22nd (menunest-189 review finding): the
    // old version derived "today" from real DateTime.UtcNow, so on the 1st
    // calendar day of a month `earlierThisMonth == today` and the mutation
    // this test exists to catch (`row.FrozenOn != today` instead of
    // `!row.IsForMonth(...)`) would slip through undetected on that one day.
    [Fact]
    public async Task Allowance_card_does_not_refreeze_on_a_later_read_within_the_same_month()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 8, 22, 3, 0, 0, DateTimeKind.Utc); // Aug 22 in Bangkok too
        var today = TodayIn(fx);
        today.Day.Should().NotBe(1, "the mutation this test guards against is invisible if today happens to be the 1st");

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        cat.MarkEveryday(true);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(fx.Family.Id, cat.Id, today.Year, today.Month, 6000m));
        await fx.Db.SaveChangesAsync();

        var freezer = new AllowanceFreezer(fx.Db);
        // A Budgeting event that already happened earlier THIS month — not today.
        var earlierThisMonth = new DateOnly(today.Year, today.Month, 1);
        var seeded = await freezer.RefreezeAsync(fx.Family.Id, earlierThisMonth, CancellationToken.None);
        seeded.Should().NotBeNull();
        await fx.Db.SaveChangesAsync();
        var (amountBefore, frozenOnBefore, frozenPotBefore) = (seeded!.Amount, seeded.FrozenOn, seeded.FrozenPot);

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, freezer, fx.Clock);

        await sut.Handle(new GetMonthlySummaryQuery(today.Year, today.Month, Bkk), CancellationToken.None);
        var afterFirstRead = fx.Db.DailyAllowances.Single();
        afterFirstRead.FrozenOn.Should().Be(frozenOnBefore, "reading the summary is not a Budgeting event");
        afterFirstRead.Amount.Should().Be(amountBefore);
        afterFirstRead.FrozenPot.Should().Be(frozenPotBefore);

        await sut.Handle(new GetMonthlySummaryQuery(today.Year, today.Month, Bkk), CancellationToken.None);
        var afterSecondRead = fx.Db.DailyAllowances.Single();
        afterSecondRead.FrozenOn.Should().Be(frozenOnBefore, "a second read must be just as inert as the first");
        afterSecondRead.Amount.Should().Be(amountBefore);
        afterSecondRead.FrozenPot.Should().Be(frozenPotBefore);
    }

    [Fact]
    public async Task Allowance_card_pace_delta_is_wired_from_the_frozen_rows_own_calculation()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 8, 22, 3, 0, 0, DateTimeKind.Utc);
        var today = TodayIn(fx);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        cat.MarkEveryday(true);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(fx.Family.Id, cat.Id, today.Year, today.Month, 6000m));
        var acc = BudgetAccount.Create(fx.Family.Id, "Checking", BudgetAccountType.Cash, 0m, 0);
        fx.Db.BudgetAccounts.Add(acc);
        await fx.Db.SaveChangesAsync();

        var freezer = new AllowanceFreezer(fx.Db);
        // Freeze as of the 1st so there is at least one completed day to pace
        // against — today (the 22nd, per the fixed clock above) is well past it.
        var frozenOn = new DateOnly(today.Year, today.Month, 1);
        (await freezer.RefreezeAsync(fx.Family.Id, frozenOn, CancellationToken.None)).Should().NotBeNull();
        await fx.Db.SaveChangesAsync();

        fx.Db.BudgetTransactions.Add(BudgetTransaction.Create(
            fx.Family.Id, acc.Id, cat.Id, -500m, frozenOn, "Spent", fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, freezer, fx.Clock);

        var result = await sut.Handle(
            new GetMonthlySummaryQuery(today.Year, today.Month, Bkk), CancellationToken.None);

        result.DailyAllowance.Should().NotBeNull();
        result.DailyAllowance!.HasMarks.Should().BeTrue();

        // The handler must not restate DailyAllowance.PaceDelta's formula — it
        // has to feed it the SAME pot/date this test computes independently.
        var expectedCurrentPot = await freezer.CurrentPotAsync(fx.Family.Id, today, CancellationToken.None);
        var expectedRow = fx.Db.DailyAllowances.Single();
        result.DailyAllowance.PaceDelta.Should().Be(expectedRow.PaceDelta(expectedCurrentPot, today));
        result.DailyAllowance.Amount.Should().Be(expectedRow.Amount);
        result.DailyAllowance.FrozenOn.Should().Be(expectedRow.FrozenOn);
    }

    // ── menunest-189: the viewer's local day, not the server's UTC day ──────

    /// <summary>
    /// The exact boundary menunest-189 exists for: 2026-08-31T20:00Z is
    /// 2026-09-01T03:00 in Bangkok (UTC+7) — the server's UTC day (Aug 31) and
    /// the viewer's local day (Sep 1) disagree. If the handler read the UTC day
    /// (or converted with the wrong sign), a request for September — the
    /// viewer's real "this month" at this instant — would never match "today"
    /// and the card would stay null; the freeze would also land on the wrong
    /// day and divide by the wrong day count.
    /// </summary>
    [Fact]
    public async Task Allowance_card_renders_using_the_Bangkok_date_when_UTC_is_still_the_previous_day()
    {
        using var fx = new HandlerTestFixture();
        fx.Clock.UtcNow = new DateTime(2026, 8, 31, 20, 0, 0, DateTimeKind.Utc);

        var group = BudgetCategoryGroup.Create(fx.Family.Id, "Everyday", 0);
        fx.Db.BudgetCategoryGroups.Add(group);
        var cat = BudgetCategory.Create(fx.Family.Id, group.Id, "Groceries", null, 0);
        cat.MarkEveryday(true);
        fx.Db.BudgetCategories.Add(cat);
        fx.Db.MonthlyAssignments.Add(
            MonthlyAssignment.Create(fx.Family.Id, cat.Id, 2026, 9, 3000m));
        await fx.Db.SaveChangesAsync();

        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        // September is "today" in Bangkok even though the server's UTC clock is
        // still in August — this is the request the viewer's phone actually sends.
        var septemberResult = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 9, Bkk), CancellationToken.None);

        septemberResult.DailyAllowance.Should().NotBeNull(
            "the card must render off the VIEWER's day, not the server's still-August UTC day");
        septemberResult.DailyAllowance!.HasMarks.Should().BeTrue();
        septemberResult.DailyAllowance.FrozenOn.Should().Be(new DateOnly(2026, 9, 1),
            "the freeze must use the Bangkok date (Sep 1), not the UTC date (Aug 31)");
        // 30 days in September, frozen on the 1st → the full 30 remain. A
        // UTC-day freeze (Aug 31, 1 day "remaining" in August) would divide
        // by an entirely different, wrong number.
        septemberResult.DailyAllowance.Amount.Should().Be(3000m / 30m);

        // The other half of the same bug: August must NOT be treated as
        // current once the viewer's day has rolled to September.
        var augustResult = await sut.Handle(
            new GetMonthlySummaryQuery(2026, 8, Bkk), CancellationToken.None);
        augustResult.DailyAllowance.Should().BeNull(
            "August is not the viewer's current month once the local day has rolled to September");
    }

    [Fact]
    public async Task Unknown_time_zone_id_is_rejected_not_silently_defaulted()
    {
        using var fx = new HandlerTestFixture();
        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var act = async () => await sut.Handle(
            new GetMonthlySummaryQuery(2026, 1, "Not/A/Real/Zone"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Missing_time_zone_id_is_rejected_not_silently_defaulted_to_UTC()
    {
        using var fx = new HandlerTestFixture();
        var sut = new GetMonthlySummaryHandler(fx.Db, fx.UserProvisioner.Object, new AllowanceFreezer(fx.Db), fx.Clock);

        var act = async () => await sut.Handle(
            new GetMonthlySummaryQuery(2026, 1, null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }
}
