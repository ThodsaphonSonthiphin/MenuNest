using Mediator;

namespace MenuNest.Application.UseCases.Chat.DeleteConversation;

public sealed record DeleteConversationCommand(Guid Id) : ICommand;
