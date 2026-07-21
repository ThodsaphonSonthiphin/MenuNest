using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.RetimeStopToHour;

public sealed class RetimeStopToHourHandler : ICommandHandler<RetimeStopToHourCommand, RetimeResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<RetimeStopToHourCommand> _validator;

    public RetimeStopToHourHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<RetimeStopToHourCommand> validator)
    { _db = db; _users = users; _validator = validator; }

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

        var startBefore = trip.StartDate;
        var deltaDays = c.NewAnchorDate.DayNumber - day.Date.DayNumber;
        var moved = deltaDays != 0;

        // (cross-day realign added in Task 4)

        day.SetStartTime(c.NewDayStartTime);
        day.SetUseCurrentTimeAsStart(false);            // pin (ADR-110)

        await _db.SaveChangesAsync(ct);
        return new RetimeResultDto(moved, startBefore, trip.StartDate, day.Date, day.DayStartTime);
    }
}
