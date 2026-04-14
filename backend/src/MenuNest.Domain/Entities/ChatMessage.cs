using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;

namespace MenuNest.Domain.Entities;

public sealed class ChatMessage : Entity
{
    public Guid ConversationId { get; private set; }
    public ChatRole Role { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string? ToolCalls { get; private set; }
    public string? ToolName { get; private set; }
    public string? StructuredData { get; private set; }

    private ChatMessage() { }

    public static ChatMessage CreateUserMessage(Guid conversationId, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new Exceptions.DomainException("Message content cannot be empty.");

        return new ChatMessage
        {
            ConversationId = conversationId,
            Role = ChatRole.User,
            Content = content
        };
    }

    public static ChatMessage CreateAssistantMessage(
        Guid conversationId,
        string content,
        string? toolCalls = null,
        string? structuredData = null)
    {
        return new ChatMessage
        {
            ConversationId = conversationId,
            Role = ChatRole.Assistant,
            Content = content,
            ToolCalls = toolCalls,
            StructuredData = structuredData
        };
    }

    public static ChatMessage CreateToolMessage(
        Guid conversationId,
        string toolName,
        string content)
    {
        return new ChatMessage
        {
            ConversationId = conversationId,
            Role = ChatRole.Tool,
            ToolName = toolName,
            Content = content
        };
    }
}
