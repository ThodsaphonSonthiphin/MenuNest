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

        var visitedPlaceIds = (await _db.Stops
            .Where(s => placeIds.Contains(s.TripPlaceId) && s.IsVisited)
            .Select(s => s.TripPlaceId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        var groups = rows.GroupBy(r => r.Place.GooglePlaceId ?? $"tp:{r.Place.Id}");

        var result = new List<DiscoverPlaceDto>();
        foreach (var g in groups)
        {
            var rep = g.OrderByDescending(r => r.Place.UpdatedAt ?? r.Place.CreatedAt).First().Place;
            var trips = g.Select(r => new PlaceTripRefDto(r.TripId, r.TripName))
                         .GroupBy(x => x.TripId)
                         .Select(x => x.First())
                         .ToList();
            var visited = g.Any(r => visitedPlaceIds.Contains(r.Place.Id));

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
                rep.BestTimeStart,
                rep.BestTimeEnd,
                rep.SeasonPeriods.Select(s => new SeasonPeriodDto(s.Kind, s.Months.ToList(), s.Note)).ToList(),
                visited,
                trips));
        }

        return result.OrderBy(r => r.Name).ToList();
    }
}
