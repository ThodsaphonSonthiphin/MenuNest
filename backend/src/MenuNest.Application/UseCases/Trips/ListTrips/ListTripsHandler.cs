using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.ListTrips;

public sealed class ListTripsHandler : IQueryHandler<ListTripsQuery, PagedResult<TripDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public ListTripsHandler(IApplicationDbContext db, IUserProvisioner users) { _db = db; _users = users; }

    public async ValueTask<PagedResult<TripDto>> Handle(ListTripsQuery q, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        
        var query = _db.Trips.Where(t => t.UserId == user.Id && t.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var search = q.Search; // SQL Server is case-insensitive by default
            query = query.Where(t => t.Name.Contains(search) || 
                                     (t.Destination != null && t.Destination.Contains(search)));
        }

        var isDesc = string.Equals(q.SortDirection, "Descending", StringComparison.OrdinalIgnoreCase);
        query = (q.SortColumn?.ToLowerInvariant()) switch
        {
            "name" => isDesc ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name),
            "destination" => isDesc ? query.OrderByDescending(t => t.Destination) : query.OrderBy(t => t.Destination),
            "daycount" => isDesc ? query.OrderByDescending(t => t.DayCount) : query.OrderBy(t => t.DayCount),
            _ => isDesc ? query.OrderByDescending(t => t.StartDate) : query.OrderBy(t => t.StartDate)
        };

        var count = await query.CountAsync(ct);

        var items = await query
            .Skip(q.Skip)
            .Take(q.Take > 0 ? q.Take : 10)
            .Select(t => new TripDto(t.Id, t.Name, t.Destination, t.StartDate, t.DayCount, t.DefaultTravelMode, t.IsDaily))
            .ToListAsync(ct);

        return new PagedResult<TripDto>(items, count);
    }
}
