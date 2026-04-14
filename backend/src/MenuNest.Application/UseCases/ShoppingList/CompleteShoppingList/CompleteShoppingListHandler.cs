using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.CompleteShoppingList;

public sealed class CompleteShoppingListHandler
    : ICommandHandler<CompleteShoppingListCommand, ShoppingListDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public CompleteShoppingListHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<ShoppingListDto> Handle(
        CompleteShoppingListCommand command, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var list = await _db.ShoppingLists
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == command.Id && l.FamilyId == familyId, ct)
            ?? throw new DomainException("Shopping list not found.");

        list.Complete();

        await _db.SaveChangesAsync(ct);

        return new ShoppingListDto(
            list.Id, list.Name, list.Status,
            list.Items.Count,
            list.Items.Count(i => i.IsBought),
            list.CreatedAt, list.CompletedAt);
    }
}
