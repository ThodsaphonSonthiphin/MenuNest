using MenuNest.Application.UseCases.MealPlan;
using MenuNest.Application.UseCases.MealPlan.ListMealPlan;
using MenuNest.Application.UseCases.MealPlan.CreateMealPlanEntry;
using MenuNest.Application.UseCases.MealPlan.UpdateMealPlanEntry;
using MenuNest.Application.UseCases.MealPlan.DeleteMealPlanEntry;
using MenuNest.Application.UseCases.MealPlan.StockCheck;
using MenuNest.Application.UseCases.MealPlan.StockCheckBatch;
using MenuNest.Application.UseCases.MealPlan.CookBatch;
using MenuNest.Domain.Enums;

namespace MenuNest.McpServer.Tools;

[McpServerToolType]
public sealed class MealPlanTools(IMediator mediator)
{
    [McpServerTool, Description("List meal plan entries within a date range")]
    public async Task<IReadOnlyList<MealPlanEntryDto>> list_meal_plan(
        [Description("Start date (inclusive), e.g. 2026-06-01")] DateOnly from,
        [Description("End date (inclusive), e.g. 2026-06-07")] DateOnly to,
        CancellationToken ct)
        => await mediator.Send(new ListMealPlanQuery(from, to), ct);

    [McpServerTool, Description("Add a recipe to a meal slot on a specific date")]
    public async Task<MealPlanEntryDto> create_meal_plan_entry(
        [Description("Date for the meal")] DateOnly date,
        [Description("Meal slot: Breakfast, Lunch, or Dinner")] MealSlot mealSlot,
        [Description("Recipe ID")] Guid recipeId,
        [Description("Optional notes")] string? notes,
        CancellationToken ct)
        => await mediator.Send(new CreateMealPlanEntryCommand(date, mealSlot, recipeId, notes), ct);

    [McpServerTool, Description("Update a meal plan entry's recipe or notes")]
    public async Task<MealPlanEntryDto> update_meal_plan_entry(
        [Description("Meal plan entry ID")] Guid id,
        [Description("New recipe ID")] Guid recipeId,
        [Description("New notes (optional)")] string? notes,
        CancellationToken ct)
        => await mediator.Send(new UpdateMealPlanEntryCommand(id, recipeId, notes), ct);

    [McpServerTool, Description("Remove a meal plan entry")]
    public async Task delete_meal_plan_entry(
        [Description("Meal plan entry ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteMealPlanEntryCommand(id), ct);

    [McpServerTool, Description("Check whether stock covers the ingredients for a single meal plan entry")]
    public async Task<StockCheckDto> stock_check(
        [Description("Meal plan entry ID")] Guid entryId,
        CancellationToken ct)
        => await mediator.Send(new StockCheckQuery(entryId), ct);

    [McpServerTool, Description("Check stock for multiple meal plan entries at once")]
    public async Task<StockCheckBatchDto> stock_check_batch(
        [Description("Array of meal plan entry IDs")] Guid[] entryIds,
        CancellationToken ct)
        => await mediator.Send(new StockCheckBatchQuery(entryIds), ct);

    [McpServerTool, Description("Mark meal plan entries as cooked and deduct stock")]
    public async Task<CookBatchResult> cook_batch(
        [Description("Array of meal plan entry IDs to cook")] Guid[] entryIds,
        CancellationToken ct)
        => await mediator.Send(new CookBatchCommand(entryIds), ct);
}
