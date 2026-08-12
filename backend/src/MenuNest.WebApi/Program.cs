using System.Reflection;
using System.Text.Json.Serialization;
using MenuNest.Application;
using MenuNest.Infrastructure;
using MenuNest.McpServer;
using MenuNest.WebApi;
using MenuNest.WebApi.Middleware;
using MenuNest.WebApi.Oauth;
using Microsoft.AspNetCore.Authorization;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------------------------
// Layers
// ----------------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Mediator (martinothamar/Mediator) — the source generator scans
// referenced assemblies for IRequestHandler / ICommandHandler /
// IQueryHandler implementations and wires DI for us.
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
});

// MCP Server — tools call IMediator; no new handlers or business logic
builder.Services.AddMenuNestMcpServer();

// MCP OAuth proxy (AS facade) — see docs/adr/003-mcp-oauth-proxy.md
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<MenuNest.WebApi.Oauth.EntraClient>();
builder.Services.AddScoped<MenuNest.WebApi.Oauth.ClientStore>();
builder.Services.AddSingleton<MenuNest.WebApi.Oauth.PkceStateStore>();
builder.Services.AddScoped<MenuNest.WebApi.Oauth.TokenStore>();
builder.Services.AddSingleton<MenuNest.WebApi.Oauth.OAuthJwt>();
builder.Services.AddScoped<MenuNest.WebApi.Oauth.AppSessionStore>();
builder.Services.AddScoped<MenuNest.WebApi.Oauth.AppSessionService>();

// Health module — polls FollowUpPings every minute to fire web pushes.
builder.Services.AddHostedService<MenuNest.Infrastructure.BackgroundServices.FollowUpDispatcher>();

// ----------------------------------------------------------------------
// Authentication — dual JWT bearer (Microsoft Entra ID + Google)
// A policy scheme inspects the incoming token's issuer and forwards
// to the matching JWT bearer handler.
// ----------------------------------------------------------------------
builder.Services
    .AddAuthentication("MultiAuth")
    .AddPolicyScheme("MultiAuth", "Microsoft + Google selector", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("MenuNest.Auth.PolicyScheme");
            var appIssuer = builder.Configuration["MCP:ServerUrl"] ?? string.Empty;
            var scheme = MenuNest.WebApi.Auth.BearerSchemeSelector.Select(
                context.Request.Headers.Authorization.FirstOrDefault(), appIssuer);
            logger.LogDebug("Forwarding bearer to {Scheme} scheme", scheme);
            return scheme;
        };
    })
    .AddJwtBearer("Microsoft", options =>
    {
        options.MapInboundClaims = false;
        var azureAd = builder.Configuration.GetSection("AzureAd");
        options.Authority = $"{azureAd["Instance"]}common/v2.0";
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidAudience = azureAd["ClientId"],
            ValidateIssuer = false,
        };
        // Same shape as the Google scheme below, and for the same reason: since
        // /api/session/exchange names BOTH provider schemes in its policy and
        // AuthorizationMiddleware runs every scheme in the list without
        // short-circuiting, a Google exchange also runs this handler and vice
        // versa. See the Google block for why ctx.Fail must be called.
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                ctx.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("MenuNest.Auth.Microsoft")
                    .LogWarning(ctx.Exception, "Microsoft JWT authentication failed: {Reason}", ctx.Exception.Message);
                ctx.Fail(ctx.Exception);
                return Task.CompletedTask;
            },
            OnTokenValidated = ctx =>
            {
                ctx.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("MenuNest.Auth.Microsoft")
                    .LogDebug("Microsoft JWT validated for sub {Sub}", ctx.Principal?.FindFirst("sub")?.Value);
                return Task.CompletedTask;
            },
        };
    })
    .AddJwtBearer("Google", options =>
    {
        options.MapInboundClaims = false;
        options.Authority = "https://accounts.google.com";
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidAudience = builder.Configuration["Google:ClientId"],
            ValidateIssuer = true,
            ValidIssuer = "https://accounts.google.com",
        };
        // Structured auth diagnostics. LogWarning surfaces failures in
        // Application Insights (the ApplicationInsightsLoggerProvider
        // captures ILogger Warning and above); the success path stays at
        // Debug so it is silent in production. Replaces earlier
        // Console.WriteLine probes, which only reached the App Service
        // log stream and were invisible in App Insights.
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                ctx.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("MenuNest.Auth.Google")
                    .LogWarning(ctx.Exception, "Google JWT authentication failed: {Reason}", ctx.Exception.Message);
                // ctx.Fail (which sets Result — the property itself is
                // get-only) is load-bearing, not tidiness.
                // JwtBearerHandler's outer catch — which also covers the
                // ConfigurationManager metadata fetch — rethrows when this
                // event leaves Result unset, so an unreachable
                // accounts.google.com turns into an unhandled exception and
                // ExceptionHandlingMiddleware answers 500. Since
                // /api/session/exchange authenticates against BOTH provider
                // schemes with no short-circuit, that 500 lands on a caller
                // holding a perfectly good Entra token (and symmetrically the
                // other way round).
                //
                // Trade-off, deliberate: a genuinely unreachable authority now
                // degrades to a 401 instead of a 500, which is quieter. The
                // LogWarning above is what keeps it observable — the
                // ApplicationInsightsLoggerProvider captures Warning and above,
                // so the outage is still visible in App Insights. This is not
                // swallowing the error.
                ctx.Fail(ctx.Exception);
                return Task.CompletedTask;
            },
            OnTokenValidated = ctx =>
            {
                ctx.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("MenuNest.Auth.Google")
                    .LogDebug("Google JWT validated for sub {Sub}", ctx.Principal?.FindFirst("sub")?.Value);
                return Task.CompletedTask;
            },
        };
    })
    .AddJwtBearer("McpProxy", options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters =
            new MenuNest.WebApi.Oauth.OAuthJwt(builder.Configuration).ValidationParameters();
    });

// Every request requires auth by default; opt-out with [AllowAnonymous]
// on individual endpoints (e.g. health checks).
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy("McpProxy", policy =>
    {
        policy.AddAuthenticationSchemes("McpProxy");
        policy.RequireAuthenticatedUser();
    });
});

// ----------------------------------------------------------------------
// CORS — allow the SPA (Vite dev server, Azure Static Web App in prod)
// ----------------------------------------------------------------------
const string CorsPolicyName = "SpaCors";

var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .ToList();

if (builder.Environment.IsDevelopment() && !allowedOrigins.Contains("http://localhost:5173"))
{
    allowedOrigins.Add("http://localhost:5173");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy.WithOrigins(allowedOrigins.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ----------------------------------------------------------------------
// Web
// ----------------------------------------------------------------------
builder.Services.AddApplicationInsightsTelemetry();
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // The SPA serialises enums as their string names (e.g. "Breakfast")
        // — without this converter, the model binder rejects the body and
        // ASP.NET reports "The request field is required."
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // OpenAPI document at /openapi/v1.json and Scalar UI at /scalar.
    // Scalar picks up the OpenAPI document automatically.
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("MenuNest API")
            .WithTheme(ScalarTheme.Moon)
            .WithDefaultHttpClient(ScalarTarget.JavaScript, ScalarClient.Fetch);
    });
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Public build-version probe (anonymous). Infra metadata read from the
// assembly at runtime — not a domain use case (ADR-108).
app.MapGet("/version", () =>
{
    var v = BuildVersion.Read(Assembly.GetEntryAssembly());
    return Results.Ok(new { version = v.Version, commit = v.Commit, buildTime = v.BuildTime });
}).AllowAnonymous();

// MCP — Streamable HTTP; authentication is handled by the McpProxy JWT bearer
app.MapMcp("/mcp").RequireAuthorization("McpProxy");

// OAuth proxy — discovery docs + DCR + authorize + callback + token
app.MapOAuthProxy();

// SPA durable-session endpoints (ADR-161) — exchange/refresh/logout
app.MapAppSession();

app.Run();

/// <summary>
/// Top-level statements compile into an internal <c>Program</c> class. Declaring it
/// public (and partial) makes it addressable as <c>WebApplicationFactory&lt;Program&gt;</c>
/// from the test host. Behaviour-free marker — no runtime effect.
/// </summary>
public partial class Program { }
