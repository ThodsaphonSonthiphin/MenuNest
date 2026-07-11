using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.SetDayUseCurrentTime;

public sealed class SetDayUseCurrentTimeHandler : ICommandHandler<SetDayUseCurrentTimeCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public SetDayUseCurrentTimeHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<Unit> Handle(SetDayUseCurrentTimeCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var day = await _db.ItineraryDays.FirstOrDefaultAsync(d => d.Id == c.DayId
            && _db.Trips.Any(t => t.Id == d.TripId && t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null), ct)
            ?? throw new DomainException("Itinerary day not found.");

        day.SetUseCurrentTimeAsStart(c.UseCurrentTime);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
