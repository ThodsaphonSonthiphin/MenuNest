using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Accounts.UpdateAccount;

public sealed class UpdateAccountHandler : ICommandHandler<UpdateAccountCommand, BudgetAccountDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<UpdateAccountCommand> _v;
    public UpdateAccountHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<UpdateAccountCommand> v)
    { _db = db; _users = users; _v = v; }

    public async ValueTask<BudgetAccountDto> Handle(UpdateAccountCommand c, CancellationToken ct)
    {
        await _v.ValidateAndThrowAsync(c, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);
        var acc = await _db.BudgetAccounts.FirstOrDefaultAsync(a => a.Id == c.Id && a.FamilyId == familyId, ct)
                  ?? throw new DomainException("Account not found.");
        acc.Rename(c.Name);
        acc.SetSortOrder(c.SortOrder);
        if (c.IsClosed && !acc.IsClosed) acc.Close();
        if (!c.IsClosed && acc.IsClosed) acc.Reopen();
        if (c.SetBalance.HasValue) acc.SetBalance(c.SetBalance.Value);
        await _db.SaveChangesAsync(ct);
        return new BudgetAccountDto(acc.Id, acc.Name, acc.Type, acc.Balance, acc.SortOrder, acc.IsClosed);
    }
}
