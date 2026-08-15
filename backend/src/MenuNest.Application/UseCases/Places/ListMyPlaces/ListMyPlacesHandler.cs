using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips; // SeasonPeriodDto
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Places.ListMyPlaces;

public sealed class ListMyPlacesHandler : IQueryHandler<ListMyPlacesQuery, IReadOnlyList<DiscoverPlaceDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public ListMyPlacesHandler(IApplicationDbContext db, IUserProvisioner users)
    {
        _db = db;
        _users = users;
    }

    public async ValueTask<IReadOnlyList<DiscoverPlaceDto>> Handle(ListMyPlacesQuery q, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);

        // The user's Places across all live Trips (+ owning trip name). Materialize:
        // SeasonPeriods is a backing-list value object, mapped in memory (never in SQL).
        var rows = await (from p in _db.TripPlaces
                          join t in _db.Trips on p.TripId equals t.Id
                          where t.UserId == user.Id && t.DeletedAt == null
                          select new { Place = p, TripId = t.Id, TripName = t.Name })
                         .ToListAsync(ct);

        if (rows.Count == 0) return Array.Empty<DiscoverPlaceDto>();

        var placeIds = rows.Select(r => r.Place.Id).ToList();

        // One read of the Stops table serves both the "มาแล้ว" badge and ADR-168's count.
        // The IsVisited predicate moves out of SQL deliberately: same table, same index, same
        // round trip, and the count comes back for free rather than costing a second query.
        //
        // Invariant this count leans on: stopCountByPlaceId below counts EVERY Stop row
        // with that TripPlaceId, with no TripId filter, while DeleteTripPlaceHandler (Trips/
        // DeleteTripPlace/DeleteTripPlaceHandler.cs:23-26) only deletes Stops whose ItineraryDay
        // belongs to c.TripId. The two agree only because a TripPlace belongs to exactly one
        // Trip, and AddStopHandler.cs:27 -- the sole Stop.Create call site in src/ -- enforces
        // that by requiring p.TripId == c.TripId before a Stop can be created. If that ever
        // stops holding, this count would overstate what a cascade delete actually removes,
        // and DeleteTripPlaceHandler's own scoped delete would leave orphaned Stops behind (or,
        // were the scope ever loosened, risk an FK violation removing a TripPlace that
        // something outside this Trip still references).
        var stopRows = await _db.Stops
            .Where(s => placeIds.Contains(s.TripPlaceId))
            .Select(s => new { s.TripPlaceId, s.IsVisited })
            .ToListAsync(ct);

        var visitedPlaceIds = stopRows.Where(s => s.IsVisited).Select(s => s.TripPlaceId).ToHashSet();
        var stopCountByPlaceId = stopRows.GroupBy(s => s.TripPlaceId).ToDictionary(g => g.Key, g => g.Count());

        // ADR-156 §3: GooglePlaceId still wins whenever present, so the origin key is inert for
        // the common case; it only groups place_id-less rows copied from one root.
        var groups = rows.GroupBy(r => r.Place.GooglePlaceId ?? $"tp:{r.Place.OriginTripPlaceId ?? r.Place.Id}").ToList();

        var repGpids = groups
            .Select(g => g.OrderByDescending(r => r.Place.UpdatedAt ?? r.Place.CreatedAt).First().Place.GooglePlaceId)
            .Where(id => id != null).Select(id => id!).Distinct().ToList();
        var profileByGpid = (await _db.PlaceProfiles
                .Where(p => p.UserId == user.Id && repGpids.Contains(p.GooglePlaceId))
                .ToListAsync(ct))
            .ToDictionary(p => p.GooglePlaceId);

        var result = new List<DiscoverPlaceDto>();
        foreach (var g in groups)
        {
            var rep = g.OrderByDescending(r => r.Place.UpdatedAt ?? r.Place.CreatedAt).First().Place;
            var trips = g.Select(r => new PlaceTripRefDto(
                              r.TripId,
                              r.TripName,
                              r.Place.Id,
                              stopCountByPlaceId.TryGetValue(r.Place.Id, out var n) ? n : 0))
                         .GroupBy(x => x.TripId)
                         .Select(x => x.First())
                         .ToList();
            var visited = g.Any(r => visitedPlaceIds.Contains(r.Place.Id));

            var master = rep.GooglePlaceId != null && profileByGpid.TryGetValue(rep.GooglePlaceId, out var pf) ? pf : null;
            // Empty-aware: a null OR empty master list falls back to the rep TripPlace (heals #33 pre-write-through data).
            var reviewSrc = master?.ReviewLinks is { Count: > 0 } ml ? ml : rep.ReviewLinks;
            var reviewLinks = reviewSrc.Select(r => new ReviewLinkDto(r.Url, r.Label)).ToList();
            var notes = master?.Notes ?? rep.Notes;

            result.Add(new DiscoverPlaceDto(
                g.Key,
                rep.GooglePlaceId,
                rep.Name,
                rep.Lat,
                rep.Lng,
                rep.Address,
                rep.Category,
                rep.PriceLevel,
                rep.PhotoUrl,
                rep.OpeningHoursJson,
                rep.BestTimeWindows.Select(w => new BestTimeWindowDto(w.Start, w.End, w.Note)).ToList(),
                rep.SeasonPeriods.Select(s => new SeasonPeriodDto(s.Kind, s.Months.ToList(), s.Note)).ToList(),
                visited,
                trips,
                reviewLinks,
                notes,
                rep.OriginTripPlaceId ?? rep.Id));
        }

        return result.OrderBy(r => r.Name).ToList();
    }
}
