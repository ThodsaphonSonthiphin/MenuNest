using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;

public sealed class CreateAccountHandler : ICommandHandler<CreateAccountCommand, BudgetAccountDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<CreateAccountCommand> _validator;
    private readonly IClock _clock;
    public CreateAccountHandler(
        IApplicationDbContext db, IUserProvisioner users, IValidator<CreateAccountCommand> v, IClock clock)
    { _db = db; _users = users; _validator = v; _clock = clock; }

    public async ValueTask<BudgetAccountDto> Handle(CreateAccountCommand cmd, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(cmd, ct);
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        var nextSortOrder = (await _db.BudgetAccounts
            .Where(a => a.FamilyId == familyId)
            .MaxAsync(a => (int?)a.SortOrder, ct) ?? -1) + 1;

        // menunest-183: the opening balance is a BudgetTransaction, not a stored
        // number. A derived balance whose history begins with a non-transaction
        // begins from nowhere.
        var acc = BudgetAccount.Create(familyId, cmd.Name, cmd.Type, 0m, nextSortOrder);
        _db.BudgetAccounts.Add(acc);

        if (cmd.OpeningBalance != 0m)
        {
            // menunest-189: "today" is the viewer's local day, resolved from the
            // caller's IANA zone — never the server's UTC day (this handler used
            // to read DateTime.UtcNow directly, dating an opening balance created
            // 00:00–06:59 ICT one day early, which at a month boundary corrupted
            // the very derived balance menunest-183 exists to make trustworthy).
            // Only resolved here, where it's actually used — a zero opening
            // balance writes no transaction and never needs "today" at all. No
            // silent UTC fallback — a missing or unknown id is rejected.
            var tz = BudgetTimeZone.Resolve(cmd.TimeZoneId);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz));

            _db.BudgetTransactions.Add(BudgetTransaction.Create(
                familyId, acc.Id, categoryId: null,
                amount: cmd.OpeningBalance,
                date: today,
                notes: "Opening balance",
                createdByUserId: user.Id));
            acc.AdjustBalance(cmd.OpeningBalance);   // keep the cache true
        }

        await _db.SaveChangesAsync(ct);
        return new BudgetAccountDto(acc.Id, acc.Name, acc.Type, acc.Balance, acc.SortOrder, acc.IsClosed);
    }
}
