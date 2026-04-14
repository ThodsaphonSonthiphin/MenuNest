using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.DeleteShoppingListItem;

public sealed class DeleteShoppingListItemHandler
    : ICommandHandler<DeleteShoppingListItemCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public DeleteShoppingListItemHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(
        DeleteShoppingListItemCommand command, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var list = await _db.ShoppingLists
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == command.ListId && l.FamilyId == familyId, ct)
            ?? throw new DomainException("Shopping list not found.");

        var item = list.Items.FirstOrDefault(i => i.Id == command.ItemId)
            ?? throw new DomainException("Shopping list item not found.");

        if (item.IsBought)
            throw new DomainException("Cannot delete a bought item.");

        list.RemoveItem(command.ItemId);
        _db.ShoppingListItems.Remove(item);

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
