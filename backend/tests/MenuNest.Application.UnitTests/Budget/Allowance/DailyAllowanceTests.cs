using FluentAssertions;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UnitTests.Budget.Allowance;

public class DailyAllowanceTests
{
    // menunest-181's own worked example: 6,000 over the 11 days remaining on 21 August.
    [Fact]
    public void Freeze_divides_pot_by_days_remaining_inclusive_of_today()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));

        a.Amount.Should().BeApproximately(545.4545m, 0.0001m);
        a.FrozenPot.Should().Be(6000m);
        a.ForYear.Should().Be(2026);
        a.ForMonth.Should().Be(8);
    }

    [Fact]
    public void Freeze_on_the_last_day_of_the_month_divides_by_one()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 900m, new DateOnly(2026, 8, 31));

        a.Amount.Should().Be(900m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2500)]
    public void Freeze_floors_at_zero_when_the_pot_is_empty_or_negative(decimal pot)
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), pot, new DateOnly(2026, 8, 21));

        a.Amount.Should().Be(0m);
        a.FrozenPot.Should().Be(pot); // the pot itself is recorded honestly
    }

    [Fact]
    public void CompletedDays_is_zero_on_the_freeze_day_itself()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));

        a.CompletedDays(new DateOnly(2026, 8, 21)).Should().Be(0);
    }

    [Fact]
    public void CompletedDays_counts_whole_days_since_the_freeze()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));

        a.CompletedDays(new DateOnly(2026, 8, 25)).Should().Be(4);
    }

    [Fact]
    public void CompletedDays_never_goes_negative_for_a_date_before_the_freeze()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));

        a.CompletedDays(new DateOnly(2026, 8, 19)).Should().Be(0);
    }

    [Fact]
    public void Refreeze_replaces_the_figure_the_pot_and_the_month()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));

        a.Refreeze(3000m, new DateOnly(2026, 9, 1));

        a.Amount.Should().Be(100m);   // 3000 / 30 days in September
        a.FrozenPot.Should().Be(3000m);
        a.FrozenOn.Should().Be(new DateOnly(2026, 9, 1));
        a.ForMonth.Should().Be(9);
    }

    // ── PaceDelta — menunest-186 ────────────────────────────────────────────

    [Fact]
    public void PaceDelta_is_zero_on_the_freeze_day_even_after_spending()
    {
        // No day has been completed, so nothing can be behind.
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));

        a.PaceDelta(currentPot: 6000m, today: new DateOnly(2026, 8, 21)).Should().Be(0m);
    }

    [Fact]
    public void PaceDelta_does_not_double_count_a_same_day_spend_after_the_freeze()
    {
        // THE trap this design exists to avoid: summing transactions with
        // Date >= FrozenOn would count this spend AND see it already deducted
        // from the pot, because BudgetTransaction.Date is a DateOnly.
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));

        a.PaceDelta(currentPot: 5500m, today: new DateOnly(2026, 8, 21)).Should().Be(500m);
    }

    [Fact]
    public void PaceDelta_is_negative_when_less_was_spent_than_the_completed_days_allowed()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));
        // 4 completed days x 545.4545 = 2181.81 should-have; 1800 actually spent.
        a.PaceDelta(currentPot: 4200m, today: new DateOnly(2026, 8, 25))
            .Should().BeApproximately(-381.81m, 0.01m);
    }

    [Fact]
    public void PaceDelta_is_positive_when_more_was_spent_than_the_completed_days_allowed()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 6000m, new DateOnly(2026, 8, 21));
        // 4 completed days allow 2181.81; 4000 was spent.
        a.PaceDelta(currentPot: 2000m, today: new DateOnly(2026, 8, 25))
            .Should().BeApproximately(1818.18m, 0.01m);
    }

    [Fact]
    public void PaceDelta_survives_a_pot_driven_negative_by_overspending()
    {
        var a = DailyAllowance.Freeze(Guid.NewGuid(), 1000m, new DateOnly(2026, 8, 21));

        a.PaceDelta(currentPot: -500m, today: new DateOnly(2026, 8, 22))
            .Should().BeApproximately(1500m - a.Amount, 0.01m);
    }
}
