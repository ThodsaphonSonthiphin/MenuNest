using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.BuyShoppingListItem;

public sealed class BuyShoppingListItemHandler
    : ICommandHandler<BuyShoppingListItemCommand, ShoppingListItemDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public BuyShoppingListItemHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<ShoppingListItemDto> Handle(
        BuyShoppingListItemCommand command, CancellationToken ct)
    {
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var list = await _db.ShoppingLists
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == command.ListId && l.FamilyId == familyId, ct)
            ?? throw new DomainException("Shopping list not found.");

        var item = list.Items.FirstOrDefault(i => i.Id == command.ItemId)
            ?? throw new DomainException("Shopping list item not found.");

        if (item.IsBought)
            throw new DomainException("Item is already marked as bought.");

        item.MarkBought(user.Id);

        // Increment stock
        var stockItem = await _db.StockItems
            .FirstOrDefaultAsync(s => s.FamilyId == familyId && s.IngredientId == item.IngredientId, ct);

        if (stockItem is not null)
        {
            stockItem.SetQuantity(stockItem.Quantity + item.Quantity, user.Id);
        }
        else
        {
            stockItem = StockItem.Create(familyId, item.IngredientId, item.Quantity, user.Id);
            _db.StockItems.Add(stockItem);
        }

        _db.StockTransactions.Add(StockTransaction.Create(
            familyId, item.IngredientId, item.Quantity,
            StockTransactionSource.ShoppingListBought,
            sourceRefId: item.Id, userId: user.Id));

        await _db.SaveChangesAsync(ct);

        var ingredient = await _db.Ingredients.FindAsync(new object[] { item.IngredientId }, ct);
        return new ShoppingListItemDto(
            item.Id, item.IngredientId, ingredient!.Name, ingredient.Unit,
            item.Quantity, item.IsBought, item.BoughtAt,
            item.SourceMealPlanEntryIds.Count > 0 ? item.SourceMealPlanEntryIds : null);
    }
}
