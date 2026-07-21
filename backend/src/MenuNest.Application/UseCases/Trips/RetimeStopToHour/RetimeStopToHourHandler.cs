using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.Shared;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.RetimeStopToHour;

public sealed class RetimeStopToHourHandler : ICommandHandler<RetimeStopToHourCommand, RetimeResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<RetimeStopToHourCommand> _validator;
    private readonly IClock _clock;

    public RetimeStopToHourHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<RetimeStopToHourCommand> validator, IClock clock)
    { _db = db; _users = users; _validator = validator; _clock = clock; }

    public async ValueTask<RetimeResultDto> Handle(RetimeStopToHourCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);

        var trip = await _db.Trips.FirstOrDefaultAsync(
            t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct)
            ?? throw new DomainException("Trip not found.");

        var day = await _db.ItineraryDays.FirstOrDefaultAsync(d => d.Id == c.DayId && d.TripId == trip.Id, ct)
            ?? throw new DomainException("Itinerary day not found.");

        // The anchor stop must belong to the day (defense; the client resolves the actual timing).
        var anchorExists = await _db.Stops.AnyAsync(s => s.Id == c.StopId && s.ItineraryDayId == day.Id, ct);
        if (!anchorExists) throw new DomainException("Stop not found.");

        // Guard: never rewrite the trip onto a date already in the past. A cross-day pick anchored to
        // "now" (e.g. a coolest-hour forecast window) can resolve to a large negative delta and silently
        // shift StartDate + earlier days into the past. One day of slack keeps a legitimate viewer-local
        // "today" that is still UTC-yesterday from being falsely rejected.
        if (c.NewAnchorDate < DateOnly.FromDateTime(_clock.UtcNow).AddDays(-1))
            throw new DomainException("ไม่สามารถเลื่อนไปวันที่ผ่านมาแล้ว");

        var startBefore = trip.StartDate;
        var deltaDays = c.NewAnchorDate.DayNumber - day.Date.DayNumber;
        var moved = deltaDays != 0;

        if (moved)
        {
            var newStart = trip.StartDate.AddDays(deltaDays);
            trip.Reschedule(newStart, trip.DayCount);
            // Realign every kept Day in ONE SaveChanges (collision-safe; see DayRealigner + reference_ef_relational_testing).
            var days = await _db.ItineraryDays.Where(d => d.TripId == trip.Id).OrderBy(d => d.Date).ToListAsync(ct);
            DayRealigner.RealignDays(days, newStart);
            // `day` is one of those tracked entities; its Date is now the target date.
        }

        day.SetStartTime(c.NewDayStartTime);
        day.SetUseCurrentTimeAsStart(false);            // pin (ADR-115)

        await _db.SaveChangesAsync(ct);
        return new RetimeResultDto(moved, startBefore, trip.StartDate, day.Date, day.DayStartTime);
    }
}
