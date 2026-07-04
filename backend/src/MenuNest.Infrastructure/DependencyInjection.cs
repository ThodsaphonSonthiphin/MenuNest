using Azure.Identity;
using Azure.Storage.Blobs;
using MenuNest.Application.Abstractions;
using MenuNest.Infrastructure.AI;
using MenuNest.Infrastructure.AI.Tools;
using MenuNest.Infrastructure.Authentication;
using MenuNest.Infrastructure.Maps;
using MenuNest.Infrastructure.Persistence;
using MenuNest.Infrastructure.Services;
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
        services.AddSingleton<IClock, SystemClock>();

        // AI services
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
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

        services.AddScoped<IAiChatService, GeminiChatService>();
        services.AddScoped<ISpeechTokenProvider, SpeechTokenProvider>();

        // Health module — real VAPID/WebPush sender. NullWebPushSender
        // remains in the codebase for dev/test override but is no longer
        // registered. Missing keys cause the sender to log a warning and
        // return 0 instead of throwing, so dev environments without
        // VAPID configured still work end-to-end.
        services.Configure<WebPushOptions>(configuration.GetSection(WebPushOptions.SectionName));
        services.AddScoped<IWebPushSender, WebPushSender>();

        // Photo upload (SAS). Real implementation wired only when the
        // Storage:BlobEndpoint setting is present (set by Bicep as
        // Storage__BlobEndpoint in App Service config). Dev environments
        // without storage configured fall back to a stub that throws a
        // clear error when called — DI bootstrap still succeeds so the
        // rest of the app stays usable.
        var blobEndpoint = configuration["Storage:BlobEndpoint"];
        if (!string.IsNullOrWhiteSpace(blobEndpoint))
        {
            services.AddSingleton(_ => new BlobServiceClient(
                new Uri(blobEndpoint),
                new DefaultAzureCredential()));
            services.AddScoped<IBlobSasGenerator, AzureBlobSasGenerator>();
        }
        else
        {
            services.AddScoped<IBlobSasGenerator, MissingConfigBlobSasGenerator>();
        }

        // Health module — doctor-report share tokens. HmacShareTokenService
        // throws at construction if Share:TokenSigningKey is missing/short
        // (production config issue). Tests inject IOptions<ShareOptions>
        // directly so they don't need global config.
        services.Configure<ShareOptions>(configuration.GetSection(ShareOptions.SectionName));
        services.AddScoped<IShareTokenService, HmacShareTokenService>();
        services.AddScoped<IShareUrlBuilder, ShareUrlBuilder>();

        // Maps / Place resolver. Real implementation wired only when GoogleMaps:ApiKey
        // is present. Dev environments without a key fall back to a stub that throws a
        // clear DomainException when called — DI bootstrap still succeeds so the rest
        // of the app is usable.
        services.Configure<GoogleMapsOptions>(configuration.GetSection(GoogleMapsOptions.SectionName));
        var mapsKey = configuration[$"{GoogleMapsOptions.SectionName}:ApiKey"];
        if (!string.IsNullOrWhiteSpace(mapsKey))
            services.AddScoped<IPlaceResolver, GooglePlaceResolver>();
        else
            services.AddScoped<IPlaceResolver, MissingConfigPlaceResolver>();

        // Route service — per-leg Haversine fallback always available; Google Routes API
        // (computeRoutes, not the legacy Distance Matrix API) registered when key is present.
        // Cache TTL 12 h per leg, well within the ToS 30-day caching limit.
        services.AddMemoryCache();
        if (!string.IsNullOrWhiteSpace(mapsKey))
            services.AddScoped<IRouteService, GoogleRouteService>();
        else
            services.AddScoped<IRouteService, HaversineRouteService>();

        return services;
    }
}
