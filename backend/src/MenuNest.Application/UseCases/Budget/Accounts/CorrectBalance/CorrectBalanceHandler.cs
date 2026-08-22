using System.Globalization;
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Accounts.CorrectBalance;

public sealed class CorrectBalanceHandler : ICommandHandler<CorrectBalanceCommand, BalanceCorrectionResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<CorrectBalanceCommand> _validator;
    private readonly IClock _clock;

    public CorrectBalanceHandler(
        IApplicationDbContext db, IUserProvisioner users, IValidator<CorrectBalanceCommand> validator, IClock clock)
    { _db = db; _users = users; _validator = validator; _clock = clock; }

    public async ValueTask<BalanceCorrectionResultDto> Handle(CorrectBalanceCommand cmd, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(cmd, ct);
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        var acc = await _db.BudgetAccounts.FirstOrDefaultAsync(
            a => a.Id == cmd.AccountId && a.FamilyId == familyId, ct)
            ?? throw new DomainException("Account not found.");

        // menunest-189: "today" is the viewer's local day, resolved from the
        // caller's IANA zone — never the server's UTC day. Needed even on the
        // refusal path: the derived balance and the default correction date
        // both read it. No silent UTC fallback — a missing/unknown id throws.
        var tz = BudgetTimeZone.Resolve(cmd.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz));
        var tomorrow = today.AddDays(1);

        // Task 3's derived-balance sum (GetMonthlySummaryHandler's
        // `Date < nextMonth` per-account grouping), reused here scoped to
        // this one account and rooted at today rather than a viewed month.
        var derived = await _db.BudgetTransactions
            .Where(t => t.FamilyId == familyId && t.AccountId == cmd.AccountId && t.Date < tomorrow)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var difference = cmd.ActualBalance - derived;

        if (difference == 0m)
        {
            return new BalanceCorrectionResultDto(
                Written: false, DerivedBalance: derived, Difference: 0m,
                Message: "Already correct — no adjustment needed.");
        }

        if (!cmd.Confirmed)
        {
            // The refusal text IS the question the user gets asked — it must
            // carry real numbers, formatted, and name Ready to Assign
            // explicitly (the correction is uncategorised, so it lands
            // there with no quarantine step to catch a wrong correction).
            var direction = difference > 0 ? "into" : "out of";
            var message =
                $"The recorded balance for this account is ฿{Money(derived)}. You stated ฿{Money(cmd.ActualBalance)} — " +
                $"a difference of ฿{Money(Math.Abs(difference))}, which will move {direction} Ready to Assign. " +
                "Show these numbers to the user and ask them to confirm before resending with confirmed=true.";
            return new BalanceCorrectionResultDto(
                Written: false, DerivedBalance: derived, Difference: difference, Message: message);
        }

        var tx = BudgetTransaction.Create(
            familyId, cmd.AccountId, categoryId: null, amount: difference,
            date: cmd.Date ?? today, notes: cmd.Notes ?? "Balance correction", createdByUserId: user.Id);
        _db.BudgetTransactions.Add(tx);
        acc.AdjustBalance(difference);
        await _db.SaveChangesAsync(ct);

        return new BalanceCorrectionResultDto(
            Written: true, DerivedBalance: derived, Difference: difference,
            Message: "Correction recorded — it landed in Ready to Assign.");
    }

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}
