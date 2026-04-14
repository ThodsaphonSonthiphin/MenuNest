using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.UnbuyShoppingListItem;

public sealed class UnbuyShoppingListItemHandler
    : ICommandHandler<UnbuyShoppingListItemCommand, ShoppingListItemDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public UnbuyShoppingListItemHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<ShoppingListItemDto> Handle(
        UnbuyShoppingListItemCommand command, CancellationToken ct)
    {
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var list = await _db.ShoppingLists
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == command.ListId && l.FamilyId == familyId, ct)
            ?? throw new DomainException("Shopping list not found.");

        var item = list.Items.FirstOrDefault(i => i.Id == command.ItemId)
            ?? throw new DomainException("Shopping list item not found.");

        if (!item.IsBought)
            throw new DomainException("Item has not been bought yet.");

        item.Unmark();

        // Decrease stock (clamp at 0)
        var stockItem = await _db.StockItems
            .FirstOrDefaultAsync(s => s.FamilyId == familyId && s.IngredientId == item.IngredientId, ct);

        stockItem?.ApplyDelta(-item.Quantity, user.Id);

        _db.StockTransactions.Add(StockTransaction.Create(
            familyId, item.IngredientId, -item.Quantity,
            StockTransactionSource.Correction,
            sourceRefId: item.Id, userId: user.Id,
            notes: "Unbuy shopping list item"));

        await _db.SaveChangesAsync(ct);

        var ingredient = await _db.Ingredients.FindAsync(new object[] { item.IngredientId }, ct);
        return new ShoppingListItemDto(
            item.Id, item.IngredientId, ingredient!.Name, ingredient.Unit,
            item.Quantity, item.IsBought, item.BoughtAt,
            item.SourceMealPlanEntryIds.Count > 0 ? item.SourceMealPlanEntryIds : null);
    }
}
