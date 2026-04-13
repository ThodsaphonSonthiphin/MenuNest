using MenuNest.Application.Abstractions;
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

        return services;
    }
}
