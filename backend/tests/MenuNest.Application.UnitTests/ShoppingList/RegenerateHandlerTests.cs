using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.ShoppingList.RegenerateShoppingList;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UnitTests.ShoppingList;

public class RegenerateHandlerTests
{
    [Fact]
    public async Task Preserves_bought_items_and_recomputes_unbought()
    {
        using var fx = new HandlerTestFixture();

        var egg = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        var oil = Ingredient.Create(fx.Family.Id, "น้ำมัน", "ขวด");
        fx.Db.Ingredients.AddRange(egg, oil);

        var recipe = Recipe.Create(fx.Family.Id, "ไข่ทอด", fx.User.Id);
        recipe.AddIngredient(egg.Id, 5m);
        recipe.AddIngredient(oil.Id, 2m);
        fx.Db.Recipes.Add(recipe);

        var entry = MealPlanEntry.Create(
            fx.Family.Id, new DateOnly(2026, 4, 15), MealSlot.Breakfast, recipe.Id, fx.User.Id);
        fx.Db.MealPlanEntries.Add(entry);

        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, egg.Id, 1m, fx.User.Id));
        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, oil.Id, 10m, fx.User.Id));

        var list = Domain.Entities.ShoppingList.Create(fx.Family.Id, "Test", fx.User.Id);
        fx.Db.ShoppingLists.Add(list);
        fx.Db.SaveChanges();

        // Add items: egg (source entry) and oil (source entry)
        var eggItem2 = list.AddOrIncreaseItem(egg.Id, 4m, new[] { entry.Id });  // egg: missing 4
        var oilItem = list.AddOrIncreaseItem(oil.Id, 1m, new[] { entry.Id });   // oil: was missing 1
        fx.Db.ShoppingListItems.Add(eggItem2);
        fx.Db.ShoppingListItems.Add(oilItem);
        fx.Db.SaveChanges();

        // Mark egg as bought — should be preserved
        var eggItem = list.Items.Single(i => i.IngredientId == egg.Id);
        eggItem.MarkBought(fx.User.Id);
        fx.Db.SaveChanges();

        var sut = new RegenerateShoppingListHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new RegenerateShoppingListCommand(list.Id), CancellationToken.None);

        // Egg item preserved (bought), oil item recomputed
        // Oil: required 2, stock 10 → no shortage → item removed
        result.Items.Should().ContainSingle();
        result.Items[0].IngredientId.Should().Be(egg.Id);
        result.Items[0].IsBought.Should().BeTrue();
    }

    [Fact]
    public async Task Skips_cooked_entries_during_regenerate()
    {
        using var fx = new HandlerTestFixture();

        var egg = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        fx.Db.Ingredients.Add(egg);

        var recipe = Recipe.Create(fx.Family.Id, "ไข่ทอด", fx.User.Id);
        recipe.AddIngredient(egg.Id, 3m);
        fx.Db.Recipes.Add(recipe);

        var entry = MealPlanEntry.Create(
            fx.Family.Id, new DateOnly(2026, 4, 15), MealSlot.Breakfast, recipe.Id, fx.User.Id);
        entry.MarkCooked(fx.User.Id);
        fx.Db.MealPlanEntries.Add(entry);

        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, egg.Id, 0m, fx.User.Id));

        var list = Domain.Entities.ShoppingList.Create(fx.Family.Id, "Test", fx.User.Id);
        fx.Db.ShoppingLists.Add(list);
        fx.Db.SaveChanges();

        var eggItem = list.AddOrIncreaseItem(egg.Id, 3m, new[] { entry.Id });
        fx.Db.ShoppingListItems.Add(eggItem);
        fx.Db.SaveChanges();

        var sut = new RegenerateShoppingListHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new RegenerateShoppingListCommand(list.Id), CancellationToken.None);

        // Entry was cooked → excluded → no missing items
        result.Items.Should().BeEmpty();
    }
}
