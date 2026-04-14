using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Chat.SendMessage;

public sealed class SendMessageHandler : ICommandHandler<SendMessageCommand, SendMessageResponseDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IAiChatService _aiChatService;

    public SendMessageHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IAiChatService aiChatService)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _aiChatService = aiChatService;
    }

    public async ValueTask<SendMessageResponseDto> Handle(SendMessageCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Content))
            throw new DomainException("Message content cannot be empty.");

        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        // Load and verify ownership of conversation
        var conversation = await _db.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == command.ConversationId && c.UserId == user.Id, ct)
            ?? throw new DomainException("Conversation not found.");

        // Load message history (exclude tool-role messages for the AI context)
        var history = await _db.ChatMessages
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        // Persist the user message
        var userMessage = ChatMessage.CreateUserMessage(conversation.Id, command.Content);
        _db.ChatMessages.Add(userMessage);
        await _db.SaveChangesAsync(ct);

        // Determine whether this is a confirmation of pending write actions
        var lastAssistant = history
            .LastOrDefault(m => m.Role == Domain.Enums.ChatRole.Assistant);

        AiChatResponse aiResponse;

        if (lastAssistant?.ToolCalls is not null && IsConfirmation(command.Content))
        {
            aiResponse = await _aiChatService.ExecutePendingActionsAsync(
                history,
                lastAssistant.ToolCalls,
                familyId,
                user.Id,
                ct);
        }
        else
        {
            aiResponse = await _aiChatService.ChatAsync(
                history,
                command.Content,
                familyId,
                user.Id,
                ct);
        }

        // Persist the assistant response
        var assistantMessage = ChatMessage.CreateAssistantMessage(
            conversation.Id,
            aiResponse.Content,
            aiResponse.ToolCallsJson,
            aiResponse.StructuredDataJson);

        _db.ChatMessages.Add(assistantMessage);

        // Update conversation title from the first user message, or touch UpdatedAt
        var isFirstMessage = history.Count == 0;
        if (isFirstMessage)
        {
            var titleText = command.Content.Length > 100
                ? command.Content[..100]
                : command.Content;
            conversation.UpdateTitle(titleText);
        }
        else
        {
            conversation.Touch();
        }

        await _db.SaveChangesAsync(ct);

        return new SendMessageResponseDto(
            assistantMessage.Id,
            assistantMessage.Role.ToString(),
            assistantMessage.Content,
            assistantMessage.StructuredData,
            assistantMessage.CreatedAt);
    }

    private static bool IsConfirmation(string message)
    {
        var lower = message.Trim().ToLowerInvariant();
        var confirmPatterns = new[]
        {
            "ได้เลย", "ยืนยัน", "ตกลง", "โอเค", "ok", "yes",
            "ได้", "เอา", "ทำเลย", "ใช่", "confirm", "ดำเนินการ"
        };
        return confirmPatterns.Any(p => lower.Contains(p));
    }
}
