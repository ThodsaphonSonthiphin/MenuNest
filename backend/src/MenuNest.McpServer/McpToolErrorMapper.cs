using FluentValidation;
using MenuNest.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace MenuNest.McpServer;

/// <summary>
/// MCP-boundary equivalent of the WebApi's ExceptionHandlingMiddleware. Expected
/// domain/validation exceptions thrown by tools are turned into clean, client-facing
/// tool error results and logged at Warning. Unexpected exceptions are left to
/// propagate, so the MCP SDK still records them as errors.
/// </summary>
public static class McpToolErrorMapper
{
    public const string LoggerCategory = "MenuNest.McpServer.ToolErrors";

    public static async ValueTask<CallToolResult> GuardAsync(
        string? toolName,
        IServiceProvider? services,
        Func<ValueTask<CallToolResult>> next)
    {
        try
        {
            return await next();
        }
        catch (DomainException ex)
        {
            return ToErrorResult(toolName, services, ex, ex.Message);
        }
        catch (ValidationException ex)
        {
            return ToErrorResult(toolName, services, ex, ex.Message);
        }
    }

    private static CallToolResult ToErrorResult(
        string? toolName, IServiceProvider? services, Exception ex, string message)
    {
        services?.GetService<ILoggerFactory>()
            ?.CreateLogger(LoggerCategory)
            .LogWarning(ex, "MCP tool {Tool} rejected by a domain/validation rule: {Message}", toolName, message);

        return new CallToolResult
        {
            IsError = true,
            Content = new List<ContentBlock> { new TextContentBlock { Text = message } },
        };
    }
}
