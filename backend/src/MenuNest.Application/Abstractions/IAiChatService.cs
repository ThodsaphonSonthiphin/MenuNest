using MenuNest.Domain.Entities;

namespace MenuNest.Application.Abstractions;

/// <summary>
/// Sends messages to the AI model and returns a structured response.
/// Defined in the Application layer so handlers stay decoupled from the
/// concrete Azure OpenAI implementation in Infrastructure.
/// </summary>
public sealed record AiChatResponse(
    string Content,
    string? ToolCallsJson,
    string? StructuredDataJson,
    bool HasPendingWriteActions);

public interface IAiChatService
{
    Task<AiChatResponse> ChatAsync(
        IReadOnlyList<ChatMessage> history,
        string userMessage,
        Guid familyId,
        Guid userId,
        CancellationToken ct);

    Task<AiChatResponse> ExecutePendingActionsAsync(
        IReadOnlyList<ChatMessage> history,
        string pendingToolCallsJson,
        Guid familyId,
        Guid userId,
        CancellationToken ct);
}
