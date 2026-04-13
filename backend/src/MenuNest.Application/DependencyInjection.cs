using Microsoft.Extensions.DependencyInjection;

namespace MenuNest.Application;

/// <summary>
/// Registration entry-point for the Application layer. Invoked from
/// <c>Program.cs</c>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Intentionally empty for now — use-case handlers will be
        // registered here (or by a source-generated Mediator hook)
        // as they land.
        return services;
    }
}
