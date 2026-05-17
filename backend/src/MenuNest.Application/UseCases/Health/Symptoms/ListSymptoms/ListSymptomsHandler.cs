using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Symptoms.ListSymptoms;

public sealed class ListSymptomsHandler
    : IQueryHandler<ListSymptomsQuery, IReadOnlyList<SymptomDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListSymptomsHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<SymptomDto>> Handle(
        ListSymptomsQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        // Seeds (UserId IS NULL) ∪ current user's custom rows.
        return await _db.Symptoms
            .Where(s => s.UserId == null || s.UserId == user.Id)
            .OrderBy(s => s.IsSeed ? 0 : 1)  // seeds first
            .ThenBy(s => s.Name)
            .Select(s => new SymptomDto(s.Id, s.Name, s.IsSeed))
            .ToListAsync(ct);
    }
}
