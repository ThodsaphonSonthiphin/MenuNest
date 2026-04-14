using Mediator;

namespace MenuNest.Application.UseCases.Chat.ListConversations;

public sealed record ListConversationsQuery : IQuery<IReadOnlyList<ConversationSummaryDto>>;
