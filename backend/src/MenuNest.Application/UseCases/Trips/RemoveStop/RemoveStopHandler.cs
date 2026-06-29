using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.RemoveStop;

public sealed class RemoveStopHandler : ICommandHandler<RemoveStopCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public RemoveStopHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<Unit> Handle(RemoveStopCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var stop = await _db.Stops.FirstOrDefaultAsync(s => s.Id == c.StopId
            && _db.ItineraryDays.Any(d => d.Id == s.ItineraryDayId
                && _db.Trips.Any(t => t.Id == d.TripId && t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null)), ct)
            ?? throw new DomainException("Stop not found.");

        var dayId = stop.ItineraryDayId;
        _db.Stops.Remove(stop);

        // Resequence remaining stops on the same day (no gaps)
        var remaining = await _db.Stops
            .Where(s => s.ItineraryDayId == dayId && s.Id != c.StopId)
            .OrderBy(s => s.Sequence)
            .ToListAsync(ct);
        for (var i = 0; i < remaining.Count; i++)
            remaining[i].SetSequence(i);

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
