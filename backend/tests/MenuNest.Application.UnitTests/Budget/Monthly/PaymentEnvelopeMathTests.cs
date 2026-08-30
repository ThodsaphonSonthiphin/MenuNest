using FluentAssertions;
using MenuNest.Application.UseCases.Budget.Monthly;
using MenuNest.Domain.Enums;
using Row = MenuNest.Application.UseCases.Budget.Monthly.PaymentEnvelopeMath.AccountTxRow;

namespace MenuNest.Application.UnitTests.Budget.Monthly;

public class PaymentEnvelopeMathTests
{
    private static readonly Guid Food = Guid.NewGuid();

    // Spec §4.2, walked event by event on a card carrying 20,000 of pre-budget debt.
    [Fact]
    public void The_seven_event_walk_from_the_spec()
    {
        var rows = new List<Row>();
        decimal assigned = 0m;
        decimal Available() => PaymentEnvelopeMath.Available(assigned, rows);

        rows.Add(new Row(null, -20_000m));                 // opening balance
        Available().Should().Be(0m, "pre-budget debt funds nothing");

        rows.Add(new Row(Food, -500m));                    // buy food on the card
        Available().Should().Be(500m);

        rows.Add(new Row(Food, 500m));                     // shop refunds it
        Available().Should().Be(0m);

        rows.Add(new Row(Food, -500m));                    // buy food again
        Available().Should().Be(500m);

        rows.Add(new Row(null, -300m));                    // cash advance, no envelope
        Available().Should().Be(500m, "an uncategorised outflow is unfunded debt");

        rows.Add(new Row(null, 500m));                     // pay 500
        Available().Should().Be(0m);

        assigned = 2_000m;                                 // assign toward the old debt
        Available().Should().Be(2_000m);
    }

    [Fact]
    public void A_hand_written_payment_from_before_this_feature_still_subtracts()
    {
        // No PaymentId anywhere — the maths never reads one (spec §3).
        PaymentEnvelopeMath.Available(0m, new[] { new Row(Food, -500m), new Row(null, 500m) })
            .Should().Be(0m);
    }

    [Theory]
    [InlineData(-500, 500, 0)]        // funded exactly
    [InlineData(-20_500, 500, 20_000)] // 20,000 short
    [InlineData(-500, 900, 0)]        // over-funded never goes negative
    [InlineData(0, 0, 0)]             // settled
    public void Shortfall_floors_at_zero(decimal balance, decimal available, decimal expected)
    {
        PaymentEnvelopeMath.Shortfall(balance, available).Should().Be(expected);
    }

    [Theory]
    [InlineData(BudgetAccountType.Credit, true)]
    [InlineData(BudgetAccountType.Loan, true)]
    [InlineData(BudgetAccountType.Cash, false)]
    [InlineData(BudgetAccountType.Closed, false)]
    public void Debt_types_are_credit_and_loan_only(BudgetAccountType t, bool expected)
    {
        PaymentEnvelopeMath.IsDebtType(t).Should().Be(expected);
    }
}
