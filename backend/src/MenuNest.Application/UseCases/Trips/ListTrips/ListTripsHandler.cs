using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.ListTrips;

public sealed class ListTripsHandler : IQueryHandler<ListTripsQuery, IReadOnlyList<TripDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public ListTripsHandler(IApplicationDbContext db, IUserProvisioner users) { _db = db; _users = users; }

    public async ValueTask<IReadOnlyList<TripDto>> Handle(ListTripsQuery q, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        return await _db.Trips
            .Where(t => t.UserId == user.Id && t.DeletedAt == null)
            .OrderByDescending(t => t.StartDate)
            .Select(t => new TripDto(t.Id, t.Name, t.Destination, t.StartDate, t.DayCount, t.DefaultTravelMode))
            .ToListAsync(ct);
    }
}
