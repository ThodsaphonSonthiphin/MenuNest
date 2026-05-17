using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Triggers.ListTriggers;

public sealed class ListTriggersHandler
    : IQueryHandler<ListTriggersQuery, IReadOnlyList<TriggerDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListTriggersHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<TriggerDto>> Handle(
        ListTriggersQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        return await _db.Triggers
            .Where(t => t.UserId == null || t.UserId == user.Id)
            .OrderBy(t => t.IsSeed ? 0 : 1)
            .ThenBy(t => t.Name)
            .Select(t => new TriggerDto(t.Id, t.Name, t.IsSeed))
            .ToListAsync(ct);
    }
}
