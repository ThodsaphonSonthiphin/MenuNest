using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.ShoppingList;
using MenuNest.Application.UseCases.ShoppingList.CreateShoppingList;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UnitTests.ShoppingList;

public class CreateShoppingListHandlerTests
{
    [Fact]
    public async Task Creates_empty_list_when_no_dates_provided()
    {
        using var fx = new HandlerTestFixture();
        var sut = new CreateShoppingListHandler(fx.Db, fx.UserProvisioner.Object,
            new CreateShoppingListValidator());

        var result = await sut.Handle(
            new CreateShoppingListCommand("Test list", null, null), CancellationToken.None);

        result.Name.Should().Be("Test list");
        result.Status.Should().Be(ShoppingListStatus.Active);
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Auto_generates_items_from_planned_entries_missing_stock()
    {
        using var fx = new HandlerTestFixture();

        var egg = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        var oil = Ingredient.Create(fx.Family.Id, "น้ำมัน", "ขวด");
        fx.Db.Ingredients.AddRange(egg, oil);

        var recipe = Recipe.Create(fx.Family.Id, "ไข่ทอด", fx.User.Id);
        recipe.AddIngredient(egg.Id, 5m);
        recipe.AddIngredient(oil.Id, 1m);
        fx.Db.Recipes.Add(recipe);

        // Stock: egg=2 (short 3), oil=10 (enough)
        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, egg.Id, 2m, fx.User.Id));
        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, oil.Id, 10m, fx.User.Id));

        var entry = MealPlanEntry.Create(
            fx.Family.Id, new DateOnly(2026, 4, 15), MealSlot.Breakfast, recipe.Id, fx.User.Id);
        fx.Db.MealPlanEntries.Add(entry);
        await fx.Db.SaveChangesAsync();

        var sut = new CreateShoppingListHandler(fx.Db, fx.UserProvisioner.Object,
            new CreateShoppingListValidator());
        var result = await sut.Handle(
            new CreateShoppingListCommand("Week shopping", new DateOnly(2026, 4, 14), new DateOnly(2026, 4, 20)),
            CancellationToken.None);

        result.TotalCount.Should().Be(1);  // only egg is short
        var detail = await fx.Db.ShoppingLists
            .Include(l => l.Items)
            .SingleAsync(l => l.Id == result.Id);
        detail.Items.Should().ContainSingle();
        detail.Items.First().IngredientId.Should().Be(egg.Id);
        detail.Items.First().Quantity.Should().Be(3m);  // 5 required - 2 on hand
    }

    [Fact]
    public async Task Cooked_entries_are_excluded_from_auto_generate()
    {
        using var fx = new HandlerTestFixture();

        var egg = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        fx.Db.Ingredients.Add(egg);

        var recipe = Recipe.Create(fx.Family.Id, "ไข่ทอด", fx.User.Id);
        recipe.AddIngredient(egg.Id, 3m);
        fx.Db.Recipes.Add(recipe);

        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, egg.Id, 0m, fx.User.Id));

        var planned = MealPlanEntry.Create(
            fx.Family.Id, new DateOnly(2026, 4, 15), MealSlot.Breakfast, recipe.Id, fx.User.Id);
        var cooked = MealPlanEntry.Create(
            fx.Family.Id, new DateOnly(2026, 4, 16), MealSlot.Lunch, recipe.Id, fx.User.Id);
        cooked.MarkCooked(fx.User.Id);
        fx.Db.MealPlanEntries.AddRange(planned, cooked);
        await fx.Db.SaveChangesAsync();

        var sut = new CreateShoppingListHandler(fx.Db, fx.UserProvisioner.Object,
            new CreateShoppingListValidator());
        var result = await sut.Handle(
            new CreateShoppingListCommand("Test", new DateOnly(2026, 4, 14), new DateOnly(2026, 4, 20)),
            CancellationToken.None);

        // Only the planned entry contributes (3 eggs), cooked is excluded
        result.TotalCount.Should().Be(1);
        var list = await fx.Db.ShoppingLists.Include(l => l.Items).SingleAsync(l => l.Id == result.Id);
        list.Items.First().Quantity.Should().Be(3m);
    }
}
