using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Chat.DeleteConversation;

public sealed class DeleteConversationHandler : ICommandHandler<DeleteConversationCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public DeleteConversationHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(DeleteConversationCommand command, CancellationToken ct)
    {
        var (user, _) = await _userProvisioner.RequireFamilyAsync(ct);

        var conversation = await _db.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == command.Id && c.UserId == user.Id, ct)
            ?? throw new DomainException("Conversation not found.");

        _db.ChatConversations.Remove(conversation);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
