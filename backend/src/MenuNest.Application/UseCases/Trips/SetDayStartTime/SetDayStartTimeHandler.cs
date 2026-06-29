using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.SetDayStartTime;

public sealed class SetDayStartTimeHandler : ICommandHandler<SetDayStartTimeCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public SetDayStartTimeHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<Unit> Handle(SetDayStartTimeCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var day = await _db.ItineraryDays.FirstOrDefaultAsync(d => d.Id == c.DayId
            && _db.Trips.Any(t => t.Id == d.TripId && t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null), ct)
            ?? throw new DomainException("Itinerary day not found.");

        day.SetStartTime(c.StartTime);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
