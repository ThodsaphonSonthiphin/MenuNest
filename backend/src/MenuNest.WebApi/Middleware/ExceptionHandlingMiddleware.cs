using FluentValidation;
using MenuNest.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Middleware;

/// <summary>
/// Translates domain-level exceptions into RFC 7807 ProblemDetails
/// responses so the SPA gets a predictable JSON error shape. Anything
/// we haven't classified falls through to a generic 500.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Request validation failed: {Message}", ex.Message);
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, new ValidationProblemDetails(
                ex.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest,
            });
        }
        catch (DomainException ex)
        {
            _logger.LogInformation(ex, "Domain rule rejected request: {Message}", ex.Message);
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, new ProblemDetails
            {
                Title = "Request rejected",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogInformation(ex, "Unauthorized access: {Message}", ex.Message);
            await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, new ProblemDetails
            {
                Title = "Unauthorized",
                Status = StatusCodes.Status401Unauthorized,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while processing {Path}", context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError,
            });
        }
    }

    private static Task WriteProblemAsync(HttpContext context, int statusCode, ProblemDetails problem)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(problem, problem.GetType());
    }
}
