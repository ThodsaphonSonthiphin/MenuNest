using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.ListFamilyMembers;

public sealed class ListFamilyMembersHandler
    : IQueryHandler<ListFamilyMembersQuery, IReadOnlyList<FamilyMemberDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListFamilyMembersHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<FamilyMemberDto>> Handle(
        ListFamilyMembersQuery query, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var family = await _db.Families
            .AsNoTracking()
            .FirstAsync(f => f.Id == familyId, ct);

        var members = await _db.Users
            .AsNoTracking()
            .Where(u => u.FamilyId == familyId)
            .OrderBy(u => u.JoinedAt)
            .ToListAsync(ct);

        var relationships = await _db.UserRelationships
            .AsNoTracking()
            .Where(r => r.FamilyId == familyId)
            .ToListAsync(ct);

        return members.Select(m => new FamilyMemberDto(
            UserId: m.Id,
            DisplayName: m.DisplayName,
            Email: m.Email,
            JoinedAt: m.JoinedAt ?? m.CreatedAt,
            IsCreator: m.Id == family.CreatedByUserId,
            Relationships: relationships
                .Where(r => r.FromUserId == m.Id)
                .Select(r => new RelationshipLabelDto(
                    RelationshipId: r.Id,
                    RelationType: r.RelationType.ToString(),
                    Label: GetThaiLabel(r.RelationType)))
                .ToArray()
        )).ToList();
    }

    private static string GetThaiLabel(RelationType type) => type switch
    {
        RelationType.Parent => "พ่อ/แม่",
        RelationType.Child => "ลูก",
        RelationType.Spouse => "คู่สมรส",
        RelationType.Sibling => "พี่น้อง",
        _ => "อื่นๆ",
    };
}
