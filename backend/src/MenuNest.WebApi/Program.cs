using MenuNest.Application;
using MenuNest.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
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
// Authentication — Entra ID JWT bearer
// Multi-tenant + personal Microsoft accounts: the issuer varies per
// tenant, so we disable issuer validation here. A stricter per-tenant
// allow-list can be layered on top in a follow-up.
// ----------------------------------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services
    .Configure<JwtBearerOptions>(
        JwtBearerDefaults.AuthenticationScheme,
        options =>
        {
            options.TokenValidationParameters.ValidateIssuer = false;
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
builder.Services.AddControllers();
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

app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
