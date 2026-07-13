using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
namespace MenuNest.Application.UseCases.Trips.ListTripPlaces;

public sealed class ListTripPlacesHandler : IQueryHandler<ListTripPlacesQuery, IReadOnlyList<TripPlaceDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public ListTripPlacesHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<IReadOnlyList<TripPlaceDto>> Handle(ListTripPlacesQuery c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");

        var places = await _db.TripPlaces
            .Where(p => p.TripId == c.TripId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var placeIds = places.Select(p => p.Id).ToList();
        var entries = await (from e in _db.PlaceChecklistEntries
                             join i in _db.ChecklistItems on e.ChecklistItemId equals i.Id
                             where placeIds.Contains(e.TripPlaceId)
                             orderby e.CreatedAt, e.Id
                             select new { e.TripPlaceId, Dto = new PlaceChecklistEntryDto(e.Id, e.ChecklistItemId, i.Name, e.IsChecked) })
                            .ToListAsync(ct);
        var byPlace = entries.GroupBy(x => x.TripPlaceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PlaceChecklistEntryDto>)g.Select(x => x.Dto).ToList());

        var profiledIds = (await _db.PlaceProfiles
            .Where(p => p.UserId == user.Id)
            .Select(p => p.GooglePlaceId)
            .ToListAsync(ct)).ToHashSet();

        return places
            .Select(p => AddTripPlaceHandler.ToDto(
                p,
                byPlace.TryGetValue(p.Id, out var l) ? l : Array.Empty<PlaceChecklistEntryDto>(),
                p.GooglePlaceId != null && profiledIds.Contains(p.GooglePlaceId)))
            .ToList();
    }
}