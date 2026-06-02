using Microsoft.Extensions.DependencyInjection;

namespace MenuNest.McpServer;

public static class McpServerRegistration
{
    public static IMcpServerBuilder AddMenuNestMcpServer(this IServiceCollection services)
        => services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<Tools.RecipeTools>()
            .WithTools<Tools.IngredientTools>()
            .WithTools<Tools.MealPlanTools>()
            .WithTools<Tools.StockTools>()
            .WithTools<Tools.ShoppingListTools>()
            .WithTools<Tools.BudgetTools>();
}
