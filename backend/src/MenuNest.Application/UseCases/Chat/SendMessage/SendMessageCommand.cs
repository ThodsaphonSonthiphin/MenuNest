using Mediator;

namespace MenuNest.Application.UseCases.Chat.SendMessage;

public sealed record SendMessageCommand(Guid ConversationId, string Content) : ICommand<SendMessageResponseDto>;
