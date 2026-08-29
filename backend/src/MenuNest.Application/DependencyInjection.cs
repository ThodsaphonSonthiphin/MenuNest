using System.Reflection;
using FluentValidation;
using MenuNest.Application.UseCases.Budget.Allowance;
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
        // FluentValidation: discover every AbstractValidator<T> in the
        // Application assembly and register it with scoped lifetime.
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        // Mediator handlers are registered automatically by the
        // source generator in Program.cs (AddMediator).

        // Application services
        services.AddScoped<AllowanceFreezer>();
        services.AddScoped<UseCases.Budget.History.BudgetChangeRecorder>();
        services.AddScoped<UseCases.Budget.History.BudgetChangeApplier>();

        return services;
    }
}
