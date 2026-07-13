using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
namespace MenuNest.Application.UseCases.Trips.PushPlaceProfile;

/// <summary>
/// Push-to-master (ADR-064): overwrite the user''s PlaceProfile with the current TripPlace''s
/// enrichment (best-time + review links + checklist item-set), so future captures start from it.
/// </summary>
public sealed class PushPlaceProfileHandler : ICommandHandler<PushPlaceProfileCommand, TripPlaceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public PushPlaceProfileHandler(IApplicationDbContext db, IUserProvisioner users) { _db = db; _users = users; }

    public async ValueTask<TripPlaceDto> Handle(PushPlaceProfileCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");
        var place = await _db.TripPlaces.FirstOrDefaultAsync(p => p.Id == c.PlaceId && p.TripId == c.TripId, ct)
            ?? throw new DomainException("Place not found.");

        await PlaceProfileSync.UpsertFromAsync(_db, user.Id, place, ct);
        await _db.SaveChangesAsync(ct);

        var checklist = await (from e in _db.PlaceChecklistEntries
                               join i in _db.ChecklistItems on e.ChecklistItemId equals i.Id
                               where e.TripPlaceId == place.Id
                               orderby e.CreatedAt, e.Id
                               select new PlaceChecklistEntryDto(e.Id, e.ChecklistItemId, i.Name, e.IsChecked)).ToListAsync(ct);
        return AddTripPlaceHandler.ToDto(place, checklist, hasProfile: true);
    }
}