using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.UpdateStop;

public sealed class UpdateStopHandler : ICommandHandler<UpdateStopCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public UpdateStopHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<Unit> Handle(UpdateStopCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var stop = await _db.Stops.FirstOrDefaultAsync(s => s.Id == c.StopId
            && _db.ItineraryDays.Any(d => d.Id == s.ItineraryDayId
                && _db.Trips.Any(t => t.Id == d.TripId && t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null)), ct)
            ?? throw new DomainException("Stop not found.");

        if (c.DwellMinutes.HasValue)
            stop.SetDwell(c.DwellMinutes.Value);
        if (c.TravelModeToReach.HasValue)
            stop.SetTravelMode(c.TravelModeToReach.Value);

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
