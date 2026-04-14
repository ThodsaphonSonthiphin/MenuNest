using MenuNest.Domain.Entities;

namespace MenuNest.Infrastructure.AI;

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
