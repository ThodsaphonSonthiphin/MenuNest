using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Budget.Monthly;

/// <summary>
/// The whole of issue #112's arithmetic, kept pure so it is testable without a
/// DbContext (spec §4.2–§4.4). Nothing here reads PaymentId: pairing is for
/// finding and deleting a payment, never for computing one — which is why
/// payments hand-written before this feature shipped still subtract correctly.
/// </summary>
public static class PaymentEnvelopeMath
{
    public readonly record struct AccountTxRow(Guid? CategoryId, decimal Amount);

    /// <summary>Credit and Loan leave Ready to Assign (menunest-203, menunest-206).</summary>
    public static bool IsDebtType(BudgetAccountType t) =>
        t is BudgetAccountType.Credit or BudgetAccountType.Loan;

    /// <summary>
    /// Available = assigned − Σ(categorised rows) − Σ(uncategorised POSITIVE rows).
    /// Both minuses are correct: a categorised outflow is negative, so subtracting
    /// it adds. <paramref name="accountRows"/> is every transaction on the Credit
    /// account up to the end of the month being viewed.
    /// </summary>
    public static decimal Available(decimal assigned, IEnumerable<AccountTxRow> accountRows)
    {
        decimal categorised = 0m, uncategorisedInflow = 0m;
        foreach (var r in accountRows)
        {
            if (r.CategoryId.HasValue) categorised += r.Amount;
            else if (r.Amount > 0m) uncategorisedInflow += r.Amount;
        }
        return assigned - categorised - uncategorisedInflow;
    }

    /// <summary>What is still owed and not yet funded. Floors at 0 (spec §4.3).</summary>
    public static decimal Shortfall(decimal accountBalance, decimal available) =>
        Math.Max(0m, -accountBalance - available);
}
