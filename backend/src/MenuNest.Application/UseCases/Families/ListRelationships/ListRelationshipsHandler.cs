using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.ListRelationships;

public sealed class ListRelationshipsHandler
    : IQueryHandler<ListRelationshipsQuery, IReadOnlyList<RelationshipDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListRelationshipsHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<RelationshipDto>> Handle(
        ListRelationshipsQuery query, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var items = await _db.UserRelationships
            .AsNoTracking()
            .Where(r => r.FamilyId == familyId)
            .Join(_db.Users, r => r.FromUserId, u => u.Id,
                (r, from) => new { r, FromName = from.DisplayName })
            .Join(_db.Users, x => x.r.ToUserId, u => u.Id,
                (x, to) => new RelationshipDto(
                    x.r.Id,
                    x.r.FromUserId,
                    x.FromName,
                    x.r.ToUserId,
                    to.DisplayName,
                    x.r.RelationType.ToString()))
            .ToListAsync(ct);

        return items;
    }
}
