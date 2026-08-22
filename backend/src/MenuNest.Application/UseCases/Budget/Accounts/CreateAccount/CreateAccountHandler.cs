using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;

public sealed class CreateAccountHandler : ICommandHandler<CreateAccountCommand, BudgetAccountDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<CreateAccountCommand> _validator;
    public CreateAccountHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<CreateAccountCommand> v)
    { _db = db; _users = users; _validator = v; }

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
            _db.BudgetTransactions.Add(BudgetTransaction.Create(
                familyId, acc.Id, categoryId: null,
                amount: cmd.OpeningBalance,
                date: DateOnly.FromDateTime(DateTime.UtcNow),
                notes: "Opening balance",
                createdByUserId: user.Id));
            acc.AdjustBalance(cmd.OpeningBalance);   // keep the cache true
        }

        await _db.SaveChangesAsync(ct);
        return new BudgetAccountDto(acc.Id, acc.Name, acc.Type, acc.Balance, acc.SortOrder, acc.IsClosed);
    }
}
