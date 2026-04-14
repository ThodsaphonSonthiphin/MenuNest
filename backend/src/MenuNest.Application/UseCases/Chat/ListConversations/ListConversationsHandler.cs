using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Chat.ListConversations;

public sealed class ListConversationsHandler : IQueryHandler<ListConversationsQuery, IReadOnlyList<ConversationSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListConversationsHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<ConversationSummaryDto>> Handle(ListConversationsQuery query, CancellationToken ct)
    {
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        return await _db.ChatConversations
            .Where(c => c.UserId == user.Id && c.FamilyId == familyId)
            .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .Select(c => new ConversationSummaryDto(c.Id, c.Title, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(ct);
    }
}
