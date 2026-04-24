using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Transactions.CreateTransaction;

public sealed class CreateTransactionHandler
    : ICommandHandler<CreateTransactionCommand, BudgetTransactionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<CreateTransactionCommand> _v;
    public CreateTransactionHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<CreateTransactionCommand> v)
    { _db = db; _users = users; _v = v; }

    public async ValueTask<BudgetTransactionDto> Handle(CreateTransactionCommand c, CancellationToken ct)
    {
        await _v.ValidateAndThrowAsync(c, ct);
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        var acc = await _db.BudgetAccounts.FirstOrDefaultAsync(
            a => a.Id == c.AccountId && a.FamilyId == familyId, ct)
            ?? throw new DomainException("Account not found.");

        if (c.CategoryId.HasValue)
        {
            var catOk = await _db.BudgetCategories.AnyAsync(
                x => x.Id == c.CategoryId && x.FamilyId == familyId, ct);
            if (!catOk) throw new DomainException("Category not found.");
        }

        var tx = BudgetTransaction.Create(familyId, c.AccountId, c.CategoryId, c.Amount, c.Date, c.Notes, user.Id);
        _db.BudgetTransactions.Add(tx);
        acc.AdjustBalance(c.Amount);
        await _db.SaveChangesAsync(ct);

        return await TransactionDtoQuery.ByIdAsync(_db, tx.Id, ct);
    }
}
