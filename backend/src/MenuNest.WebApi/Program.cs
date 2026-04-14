using System.Text.Json.Serialization;
using MenuNest.Application;
using MenuNest.Infrastructure;
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

app.Run();
