using MenuNest.Application.Abstractions;
using MenuNest.Infrastructure.AI;
using MenuNest.Infrastructure.AI.Tools;
using MenuNest.Infrastructure.Authentication;
using MenuNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MenuNest.Infrastructure;

/// <summary>
/// Registration entry-point for the Infrastructure layer. Invoked
/// from <c>Program.cs</c>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured. " +
                "Set it in appsettings.Development.json or via environment variables.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        // Expose the same AppDbContext via the Application-layer
        // IApplicationDbContext interface so handlers stay decoupled
        // from the concrete Infrastructure type.
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IUserProvisioner, UserProvisioner>();

        // AI services
        services.Configure<AzureOpenAiOptions>(configuration.GetSection(AzureOpenAiOptions.SectionName));
        services.Configure<AzureSpeechOptions>(configuration.GetSection(AzureSpeechOptions.SectionName));
        services.AddHttpClient();

        // Register all AI tools
        services.AddScoped<IToolDefinition, SearchRecipesTool>();
        services.AddScoped<IToolDefinition, CheckStockTool>();
        services.AddScoped<IToolDefinition, GetMealPlanTool>();
        services.AddScoped<IToolDefinition, GetShoppingListsTool>();
        services.AddScoped<IToolDefinition, GetFamilyInfoTool>();
        services.AddScoped<IToolDefinition, CreateRecipeTool>();
        services.AddScoped<IToolDefinition, AddToMealPlanTool>();
        services.AddScoped<IToolDefinition, CreateShoppingListTool>();
        services.AddScoped<IToolDefinition, AddShoppingItemsTool>();

        services.AddScoped<IAiChatService, AzureOpenAiChatService>();
        services.AddScoped<ISpeechTokenProvider, SpeechTokenProvider>();

        return services;
    }
}
