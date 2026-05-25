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
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var nextSortOrder = (await _db.BudgetAccounts
            .Where(a => a.FamilyId == familyId)
            .MaxAsync(a => (int?)a.SortOrder, ct) ?? -1) + 1;

        var acc = BudgetAccount.Create(familyId, cmd.Name, cmd.Type, cmd.OpeningBalance, nextSortOrder);
        _db.BudgetAccounts.Add(acc);
        await _db.SaveChangesAsync(ct);
        return new BudgetAccountDto(acc.Id, acc.Name, acc.Type, acc.Balance, acc.SortOrder, acc.IsClosed);
    }
}
