using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.ReorderStops;

public sealed class ReorderStopsHandler : ICommandHandler<ReorderStopsCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public ReorderStopsHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<Unit> Handle(ReorderStopsCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var ownsDay = await _db.ItineraryDays.AnyAsync(d => d.Id == c.DayId
            && _db.Trips.Any(t => t.Id == d.TripId && t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null), ct);
        if (!ownsDay) throw new DomainException("Itinerary day not found.");

        var stops = await _db.Stops.Where(s => s.ItineraryDayId == c.DayId).ToListAsync(ct);

        var stopIds = stops.Select(s => s.Id).ToHashSet();
        if (c.OrderedStopIds.Count != stops.Count || !c.OrderedStopIds.All(stopIds.Contains))
            throw new DomainException("The reorder list must include exactly the stops of this day.");

        for (var i = 0; i < c.OrderedStopIds.Count; i++)
        {
            var stop = stops.FirstOrDefault(s => s.Id == c.OrderedStopIds[i])
                ?? throw new DomainException("Stop does not belong to this day.");
            stop.SetSequence(i);
        }
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
