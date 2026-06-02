using Microsoft.Extensions.DependencyInjection;

namespace MenuNest.McpServer;

public static class McpServerRegistration
{
    public static IMcpServerBuilder AddMenuNestMcpServer(this IServiceCollection services)
        => services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<Tools.RecipeTools>();
    // Tool registrations (.WithTools<T>()) are added incrementally in Tasks 3–8.
}
