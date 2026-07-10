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
            .WithTools<Tools.BudgetTools>()
            .WithTools<Tools.TripTools>()
            // Translate expected domain/validation exceptions from tools into clean
            // tool error results (mirrors the WebApi ExceptionHandlingMiddleware). See
            // docs/superpowers/specs/2026-06-03-mcp-oauth-personal-account-identity-design.md (decision D3).
            .WithRequestFilters(filters =>
                filters.AddCallToolFilter(next => (context, ct) =>
                    McpToolErrorMapper.GuardAsync(
                        context.Params?.Name,
                        context.Services,
                        () => next(context, ct))));
}
