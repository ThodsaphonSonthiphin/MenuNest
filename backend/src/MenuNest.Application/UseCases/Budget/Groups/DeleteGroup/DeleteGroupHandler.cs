using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Groups.DeleteGroup;

public sealed class DeleteGroupHandler : ICommandHandler<DeleteGroupCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public DeleteGroupHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<Unit> Handle(DeleteGroupCommand c, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);
        var group = await _db.BudgetCategoryGroups
            .FirstOrDefaultAsync(g => g.Id == c.Id && g.FamilyId == familyId, ct)
            ?? throw new DomainException("Group not found.");
        var hasCategories = await _db.BudgetCategories.AnyAsync(cat => cat.GroupId == c.Id, ct);
        if (hasCategories)
            throw new DomainException("Cannot delete group with categories — move or delete the categories first.");
        _db.BudgetCategoryGroups.Remove(group);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
