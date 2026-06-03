using System.Text.Json.Serialization;
using MenuNest.Application;
using MenuNest.Infrastructure;
using MenuNest.McpServer;
using MenuNest.WebApi.Middleware;
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
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            {
                var token = authHeader["Bearer ".Length..];
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                if (handler.CanReadToken(token))
                {
                    var jwt = handler.ReadJwtToken(token);
                    Console.WriteLine($"[POLICY SCHEME] issuer={jwt.Issuer}, forwarding to={(jwt.Issuer == "https://accounts.google.com" ? "Google" : "Microsoft")}");
                    if (jwt.Issuer == "https://accounts.google.com")
                        return "Google";
                }
                else
                {
                    Console.WriteLine("[POLICY SCHEME] CanReadToken=false");
                }
            }
            else
            {
                Console.WriteLine("[POLICY SCHEME] No Bearer token found");
            }
            return "Microsoft";
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
        // TEMP DIAGNOSTIC — remove after debugging
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                Console.WriteLine($"[GOOGLE AUTH FAILED] {ctx.Exception.GetType().Name}: {ctx.Exception.Message}");
                return Task.CompletedTask;
            },
            OnTokenValidated = ctx =>
            {
                Console.WriteLine($"[GOOGLE AUTH OK] sub={ctx.Principal?.FindFirst("sub")?.Value}");
                return Task.CompletedTask;
            },
            OnMessageReceived = ctx =>
            {
                Console.WriteLine($"[GOOGLE AUTH] Token received, length={ctx.Token?.Length ?? 0}");
                return Task.CompletedTask;
            },
        };
    });

// Every request requires auth by default; opt-out with [AllowAnonymous]
// on individual endpoints (e.g. health checks).
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
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

// MCP — Streamable HTTP; authentication is handled by the existing JwtBearer middleware
app.MapMcp("/mcp").RequireAuthorization();

// OAuth 2.0 discovery: Claude fetches this on first connect to learn where to authenticate.
// Intentionally Entra ID only — Google OAuth is not supported for MCP clients.
app.MapGet("/.well-known/oauth-authorization-server", () => Results.Ok(new
{
    issuer = "https://login.microsoftonline.com/common/v2.0",
    authorization_endpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
    token_endpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
    response_types_supported = new[] { "code" },
    grant_types_supported = new[] { "authorization_code", "refresh_token" },
    code_challenge_methods_supported = new[] { "S256" }
})).AllowAnonymous();

// OAuth 2.0 Protected Resource Metadata (RFC 9728): claude.ai fetches this FIRST to
// discover the authorization server + required scope before attempting login. Anonymous.
// authorization_servers points at the tenant-specific Entra issuer (concrete GUID in prod)
// — the only fully issuer-consistent discovery path. See ADR-002.
// Served at both the bare well-known path and the resource-suffixed path (RFC 9728 §3.1),
// since clients differ on which they probe.
IResult ProtectedResourceMetadata(HttpContext http)
{
    var azureAd = app.Configuration.GetSection("AzureAd");
    var resourceUrl = $"{http.Request.Scheme}://{http.Request.Host}/mcp";
    return Results.Ok(McpOAuthMetadata.Build(
        azureAd["Instance"]!, azureAd["TenantId"]!, azureAd["ClientId"]!, resourceUrl));
}

app.MapGet("/.well-known/oauth-protected-resource", ProtectedResourceMetadata).AllowAnonymous();
app.MapGet("/.well-known/oauth-protected-resource/mcp", ProtectedResourceMetadata).AllowAnonymous();

app.Run();
