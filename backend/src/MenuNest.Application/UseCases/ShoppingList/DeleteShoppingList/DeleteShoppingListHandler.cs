using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.DeleteShoppingList;

public sealed class DeleteShoppingListHandler
    : ICommandHandler<DeleteShoppingListCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public DeleteShoppingListHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(
        DeleteShoppingListCommand command, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var list = await _db.ShoppingLists
            .FirstOrDefaultAsync(l => l.Id == command.Id && l.FamilyId == familyId, ct)
            ?? throw new DomainException("Shopping list not found.");

        _db.ShoppingLists.Remove(list);

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
