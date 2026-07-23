using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.SetTripDaily;

public sealed class SetTripDailyHandler : ICommandHandler<SetTripDailyCommand, TripDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public SetTripDailyHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<TripDto> Handle(SetTripDailyCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var trip = await _db.Trips.FirstOrDefaultAsync(
            t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct)
            ?? throw new DomainException("Trip not found.");

        trip.SetDaily(c.IsDaily); // throws if enabling while DayCount > 1 (ADR-133)

        // Enabling forces the single day evergreen (ADR-132) — Trip has no Days nav,
        // so the cross-entity write is done here, not in the domain.
        if (c.IsDaily)
        {
            var day = await _db.ItineraryDays.FirstOrDefaultAsync(d => d.TripId == trip.Id, ct)
                ?? throw new DomainException("Itinerary day not found.");
            day.SetUseCurrentTimeAsStart(true);
        }

        await _db.SaveChangesAsync(ct);
        return new TripDto(trip.Id, trip.Name, trip.Destination, trip.StartDate, trip.DayCount, trip.DefaultTravelMode, trip.IsDaily);
    }
}