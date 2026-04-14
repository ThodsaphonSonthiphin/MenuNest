using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.ShoppingList.BuyShoppingListItem;
using MenuNest.Application.UseCases.ShoppingList.UnbuyShoppingListItem;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UnitTests.ShoppingList;

public class BuyUnbuyHandlerTests
{
    [Fact]
    public async Task Buy_marks_item_and_increments_stock()
    {
        using var fx = new HandlerTestFixture();
        var (list, item, ingredient) = SeedListWithItem(fx, stockOnHand: 2m, itemQty: 3m);

        var sut = new BuyShoppingListItemHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new BuyShoppingListItemCommand(list.Id, item.Id), CancellationToken.None);

        result.IsBought.Should().BeTrue();
        fx.Db.StockItems.Single(s => s.IngredientId == ingredient.Id).Quantity.Should().Be(5m); // 2+3
        fx.Db.StockTransactions.Should().ContainSingle(t =>
            t.Delta == 3m && t.Source == StockTransactionSource.ShoppingListBought);
    }

    [Fact]
    public async Task Buy_creates_stock_item_when_none_exists()
    {
        using var fx = new HandlerTestFixture();
        var ingredient = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        fx.Db.Ingredients.Add(ingredient);

        var list = Domain.Entities.ShoppingList.Create(fx.Family.Id, "Test", fx.User.Id);
        fx.Db.ShoppingLists.Add(list);
        var item = list.AddOrIncreaseItem(ingredient.Id, 5m);
        fx.Db.ShoppingListItems.Add(item);
        fx.Db.SaveChanges();

        var sut = new BuyShoppingListItemHandler(fx.Db, fx.UserProvisioner.Object);
        await sut.Handle(new BuyShoppingListItemCommand(list.Id, item.Id), CancellationToken.None);

        fx.Db.StockItems.Should().ContainSingle(s => s.IngredientId == ingredient.Id);
        fx.Db.StockItems.Single(s => s.IngredientId == ingredient.Id).Quantity.Should().Be(5m);
    }

    [Fact]
    public async Task Unbuy_reverses_stock_and_clears_bought_status()
    {
        using var fx = new HandlerTestFixture();
        var (list, item, ingredient) = SeedListWithItem(fx, stockOnHand: 5m, itemQty: 3m);

        // First buy
        item.MarkBought(fx.User.Id);
        var stock = fx.Db.StockItems.Single(s => s.IngredientId == ingredient.Id);
        stock.SetQuantity(8m, fx.User.Id); // simulate post-buy stock = 5+3
        fx.Db.SaveChanges();

        var sut = new UnbuyShoppingListItemHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new UnbuyShoppingListItemCommand(list.Id, item.Id), CancellationToken.None);

        result.IsBought.Should().BeFalse();
        fx.Db.StockItems.Single(s => s.IngredientId == ingredient.Id).Quantity.Should().Be(5m); // 8-3
        fx.Db.StockTransactions.Should().ContainSingle(t =>
            t.Delta == -3m && t.Source == StockTransactionSource.Correction);
    }

    private static (Domain.Entities.ShoppingList List, ShoppingListItem Item, Ingredient Ingredient)
        SeedListWithItem(HandlerTestFixture fx, decimal stockOnHand, decimal itemQty)
    {
        var ingredient = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        fx.Db.Ingredients.Add(ingredient);

        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, ingredient.Id, stockOnHand, fx.User.Id));

        var list = Domain.Entities.ShoppingList.Create(fx.Family.Id, "Test", fx.User.Id);
        fx.Db.ShoppingLists.Add(list);
        var item = list.AddOrIncreaseItem(ingredient.Id, itemQty);
        fx.Db.ShoppingListItems.Add(item);
        fx.Db.SaveChanges();

        return (list, list.Items.Single(), ingredient);
    }
}
