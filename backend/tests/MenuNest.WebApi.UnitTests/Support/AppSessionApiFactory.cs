using MenuNest.Infrastructure.BackgroundServices;
using MenuNest.Infrastructure.Persistence;
using MenuNest.WebApi.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
/// requires live OIDC metadata. The <c>MultiAuth</c> policy scheme still runs the
/// production <see cref="BearerSchemeSelector"/> routing — only the branches that end
/// at a provider handler are diverted, so a token this app minted is still validated
/// by the real <c>McpProxy</c> JWT bearer. The fallback policy, the exchange
/// endpoint's own policy and the claims pipeline are all untouched.</item>
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
                // The Microsoft/Google JwtBearer schemes are now named directly by the
                // exchange endpoint's policy, so their options really are constructed
                // here (they used to be unreachable behind MultiAuth's selector).
                // JwtBearerPostConfigureOptions rejects a non-HTTPS Authority, so the
                // Authority has to be well-formed even though it is never dereferenced:
                // both schemes forward to TestProviderTokenHandler before any token is
                // validated, so no OIDC metadata is ever fetched.
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:ClientId"] = "00000000-0000-0000-0000-000000000000",
                ["Google:ClientId"] = "menunest-tests.apps.googleusercontent.com",
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
            // Add the test scheme and divert the two *provider* handlers to it.
            // MultiAuth stays the default scheme, so the fallback policy, the
            // challenge path and the claims flow are all the real ones.
            services
                .AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestProviderTokenHandler>(
                    TestProviderTokenHandler.SchemeName, _ => { });

            // MultiAuth keeps running the PRODUCTION issuer routing. Only the branches
            // that land on a provider handler are diverted; a token this app minted
            // still resolves to BearerSchemeSelector.AppIssued and is validated by the
            // real McpProxy JWT bearer with the real OAuthJwt parameters. Substituting
            // the selector wholesale (as this factory used to) stubbed out the single
            // code path the durable session depends on, so nothing proved that a minted
            // token was accepted anywhere or resolved to the same user.
            services.PostConfigure<PolicySchemeOptions>("MultiAuth", options =>
                options.ForwardDefaultSelector = context =>
                {
                    var scheme = BearerSchemeSelector.Select(
                        context.Request.Headers.Authorization.FirstOrDefault(), AppIssuer);
                    return scheme == BearerSchemeSelector.AppIssued
                        ? scheme
                        : TestProviderTokenHandler.SchemeName;
                });

            // Endpoints that name the provider schemes directly (the exchange policy)
            // bypass MultiAuth entirely, so those two handlers need diverting too:
            // validating a real Microsoft/Google token needs live OIDC metadata and a
            // real signature. ForwardDefault covers authenticate AND challenge.
            foreach (var providerScheme in new[] { BearerSchemeSelector.Microsoft, BearerSchemeSelector.Google })
            {
                services.PostConfigure<JwtBearerOptions>(
                    providerScheme,
                    options => options.ForwardDefault = TestProviderTokenHandler.SchemeName);
            }
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
