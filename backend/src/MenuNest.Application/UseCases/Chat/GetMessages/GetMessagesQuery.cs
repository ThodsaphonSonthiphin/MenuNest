using Mediator;

namespace MenuNest.Application.UseCases.Chat.GetMessages;

public sealed record GetMessagesQuery(Guid ConversationId) : IQuery<IReadOnlyList<ChatMessageDto>>;
