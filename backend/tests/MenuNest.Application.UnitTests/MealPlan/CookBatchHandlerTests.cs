using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.MealPlan.CookBatch;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.MealPlan;

public class CookBatchHandlerTests
{
    [Fact]
    public async Task Sufficient_stock_marks_all_entries_cooked_and_deducts()
    {
        using var fx = new HandlerTestFixture();
        var (egg, recipe, entry) = SeedSimpleMeal(fx, eggsRequired: 2m, eggsOnHand: 5m);

        var sut = NewSut(fx);
        var result = await sut.Handle(new CookBatchCommand(new[] { entry.Id }), CancellationToken.None);

        result.CookedEntryIds.Should().ContainSingle().Which.Should().Be(entry.Id);
        result.Partial.Should().BeEmpty();
        result.Deducted.Should().ContainSingle().Which.Amount.Should().Be(2m);

        fx.Db.MealPlanEntries.Find(entry.Id)!.Status.Should().Be(MealEntryStatus.Cooked);
        fx.Db.StockItems.Single(s => s.IngredientId == egg.Id).Quantity.Should().Be(3m);
        fx.Db.StockTransactions.Should().ContainSingle(t =>
            t.IngredientId == egg.Id && t.Delta == -2m && t.Source == StockTransactionSource.Cook);
    }

    [Fact]
    public async Task Insufficient_stock_clamps_at_zero_and_writes_cook_notes()
    {
        using var fx = new HandlerTestFixture();
        var (egg, recipe, entry) = SeedSimpleMeal(fx, eggsRequired: 5m, eggsOnHand: 2m);

        var sut = NewSut(fx);
        var result = await sut.Handle(new CookBatchCommand(new[] { entry.Id }), CancellationToken.None);

        result.Partial.Should().ContainSingle().Which.Missing.Should().Be(3m);
        result.Deducted.Should().ContainSingle().Which.Amount.Should().Be(2m);

        var cooked = fx.Db.MealPlanEntries.Find(entry.Id)!;
        cooked.Status.Should().Be(MealEntryStatus.Cooked);
        cooked.CookNotes.Should().NotBeNullOrEmpty();
        cooked.CookNotes!.Should().Contain("ขาด").And.Contain(egg.Name);

        fx.Db.StockItems.Single(s => s.IngredientId == egg.Id).Quantity.Should().Be(0m);
        fx.Db.StockTransactions.Should().ContainSingle(t =>
            t.IngredientId == egg.Id && t.Delta == -2m && t.Source == StockTransactionSource.Cook);
    }

    [Fact]
    public async Task Rejects_when_any_entry_is_already_cooked()
    {
        using var fx = new HandlerTestFixture();
        var (_, _, entry) = SeedSimpleMeal(fx, eggsRequired: 1m, eggsOnHand: 5m);
        entry.MarkCooked(fx.User.Id);
        await fx.Db.SaveChangesAsync();

        var sut = NewSut(fx);
        Func<Task> act = () => sut.Handle(new CookBatchCommand(new[] { entry.Id }), CancellationToken.None).AsTask();
        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Rejects_when_any_entry_belongs_to_another_family()
    {
        using var fx = new HandlerTestFixture();
        var stranger = MealPlanEntry.Create(
            Guid.NewGuid(), new DateOnly(2026, 4, 13), MealSlot.Breakfast, Guid.NewGuid(), Guid.NewGuid());
        fx.Db.MealPlanEntries.Add(stranger);
        await fx.Db.SaveChangesAsync();

        var sut = NewSut(fx);
        Func<Task> act = () => sut.Handle(new CookBatchCommand(new[] { stranger.Id }), CancellationToken.None).AsTask();
        await act.Should().ThrowAsync<DomainException>().WithMessage("*not found*");
    }

    private static CookBatchHandler NewSut(HandlerTestFixture fx)
        => new(fx.Db, fx.UserProvisioner.Object, new CookBatchValidator());

    private static (Ingredient Egg, Recipe Recipe, MealPlanEntry Entry) SeedSimpleMeal(
        HandlerTestFixture fx, decimal eggsRequired, decimal eggsOnHand)
    {
        var egg = Ingredient.Create(fx.Family.Id, "ไข่ไก่", "ฟอง");
        fx.Db.Ingredients.Add(egg);

        var recipe = Recipe.Create(fx.Family.Id, "ไข่เจียว", fx.User.Id);
        recipe.AddIngredient(egg.Id, eggsRequired);
        fx.Db.Recipes.Add(recipe);

        fx.Db.StockItems.Add(StockItem.Create(fx.Family.Id, egg.Id, eggsOnHand, fx.User.Id));

        var entry = MealPlanEntry.Create(
            fx.Family.Id, new DateOnly(2026, 4, 13), MealSlot.Breakfast, recipe.Id, fx.User.Id);
        fx.Db.MealPlanEntries.Add(entry);
        fx.Db.SaveChanges();
        return (egg, recipe, entry);
    }
}
