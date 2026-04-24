using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Categories.DeleteCategory;

public sealed class DeleteCategoryHandler : ICommandHandler<DeleteCategoryCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public DeleteCategoryHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<Unit> Handle(DeleteCategoryCommand c, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);
        var cat = await _db.BudgetCategories
            .FirstOrDefaultAsync(x => x.Id == c.Id && x.FamilyId == familyId, ct)
            ?? throw new DomainException("Category not found.");
        var hasTx = await _db.BudgetTransactions.AnyAsync(t => t.CategoryId == c.Id, ct);
        if (hasTx)
            throw new DomainException("Cannot delete category with transactions — hide it instead.");
        _db.BudgetCategories.Remove(cat);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
