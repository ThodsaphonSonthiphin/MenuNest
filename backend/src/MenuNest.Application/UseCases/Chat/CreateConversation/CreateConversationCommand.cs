using Mediator;

namespace MenuNest.Application.UseCases.Chat.CreateConversation;

public sealed record CreateConversationCommand : ICommand<ConversationSummaryDto>;
