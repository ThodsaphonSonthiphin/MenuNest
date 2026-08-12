using MenuNest.Infrastructure.BackgroundServices;
using MenuNest.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MenuNest.WebApi.UnitTests.Support;

/// <summary>
/// Boots the real <c>Program.cs</c> in an in-process <c>TestServer</c>, so the
/// /api/session/* tests go through genuine routing, the authorization
/// <c>FallbackPolicy</c>, the real DI graph (including <c>UserProvisioner</c> and
/// <c>CurrentUserService</c>) and the app's own minimal-API JSON configuration.
///
/// Exactly two things are substituted, and both are substituted because they need
/// resources a test host cannot have — never to bypass code under test:
/// <list type="bullet">
/// <item>the SQL Server <c>AppDbContext</c> is repointed at an in-memory SQLite
/// database. It is still the production <c>AppDbContext</c> with the production
/// entity configurations applied, so <c>UserProvisioner</c> and
/// <c>AppSessionStore</c> share one real store;</item>
/// <item>the Microsoft/Google JWT bearer handlers are replaced by
/// <see cref="TestProviderTokenHandler"/>, because validating a real provider token
/// requires live OIDC metadata. The <c>MultiAuth</c> policy scheme, the fallback
/// policy and the claims pipeline are all untouched.</item>
/// </list>
/// </summary>
public sealed class AppSessionApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The app's own JWT issuer/audience in tests (production reads MCP:ServerUrl).</summary>
    public const string AppIssuer = "https://menunest-tests.invalid/mcp";

    // One connection kept open for the factory's lifetime: an in-memory SQLite
    // database lives only as long as a connection to it does.
    private readonly SqliteConnection _connection = new("Filename=:memory:");

    public AppSessionApiFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Read once by AddInfrastructure to build SQL Server options that are
                // then discarded below; no connection is ever opened with it.
                ["ConnectionStrings:DefaultConnection"] = "Server=(unused-in-tests);Database=None",
                // Required by OAuthJwt, which mints the access token exchange returns.
                ["MCP:ServerUrl"] = AppIssuer,
                ["Jwt:SigningKey"] = "menunest-app-session-integration-tests-signing-key",
                ["Cors:AllowedOrigins"] = "http://localhost:5173",
                ["ApplicationInsights:ConnectionString"] = string.Empty,
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // --- database ------------------------------------------------------
            // AddDbContext uses TryAdd for the options, so the SQL Server
            // registration must be removed before SQLite can take its place.
            foreach (var descriptor in services.Where(IsAppDbContextRegistration).ToList())
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options => options
                .UseSqlite(_connection)
                .ReplaceService<IModelCustomizer, SqliteFriendlyModelCustomizer>());

            // IApplicationDbContext is still resolved as `sp => sp.GetRequiredService<AppDbContext>()`
            // by the production registration, so AppSessionStore and UserProvisioner
            // continue to share one scoped context — as they do in production.

            // --- background work ------------------------------------------------
            // FollowUpDispatcher polls the database on a timer; irrelevant here and it
            // would race the tests' own scopes.
            foreach (var descriptor in services
                         .Where(d => d.ServiceType == typeof(IHostedService)
                                     && d.ImplementationType == typeof(FollowUpDispatcher))
                         .ToList())
            {
                services.Remove(descriptor);
            }

            // --- authentication --------------------------------------------------
            // Add the test scheme and point the *production* MultiAuth policy scheme
            // at it. MultiAuth stays the default scheme, so the fallback policy, the
            // challenge path and the claims flow are all the real ones.
            services
                .AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestProviderTokenHandler>(
                    TestProviderTokenHandler.SchemeName, _ => { });

            services.PostConfigure<PolicySchemeOptions>(
                "MultiAuth",
                options => options.ForwardDefaultSelector = _ => TestProviderTokenHandler.SchemeName);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

        return host;
    }

    /// <summary>Reads the same database the application writes to, in its own scope.</summary>
    public T Query<T>(Func<AppDbContext, T> read)
    {
        using var scope = Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static bool IsAppDbContextRegistration(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(AppDbContext)
        || descriptor.ServiceType == typeof(DbContextOptions)
        || (descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext)));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

/// <summary>
/// Lets the production <see cref="AppDbContext"/> model be created on SQLite: SQLite's
/// DDL parser rejects SQL Server's <c>nvarchar(max)</c> length token. Same rewrite the
/// existing <c>SqliteAppDbContext</c> test double performs, applied from outside so the
/// production context class itself stays untouched.
/// </summary>
internal sealed class SqliteFriendlyModelCustomizer(ModelCustomizerDependencies dependencies)
    : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(entity => entity.GetProperties()))
        {
            var columnType = property.GetColumnType();
            if (columnType is not null &&
                columnType.Contains("(max)", StringComparison.OrdinalIgnoreCase))
            {
                property.SetColumnType(null);
            }
        }
    }
}
