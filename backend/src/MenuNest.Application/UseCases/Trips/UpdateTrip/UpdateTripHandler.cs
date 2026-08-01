using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.Shared;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.UpdateTrip;

public sealed class UpdateTripHandler : ICommandHandler<UpdateTripCommand, TripDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<UpdateTripCommand> _validator;
    private readonly IClock _clock;

    public UpdateTripHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<UpdateTripCommand> validator, IClock clock)
    { _db = db; _users = users; _validator = validator; _clock = clock; }

    public async ValueTask<TripDto> Handle(UpdateTripCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var trip = await _db.Trips
            .FirstOrDefaultAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct)
            ?? throw new DomainException("Trip not found.");

        // Guard: the start date may move, but never onto a date already in the past — a
        // Backdate (ADR-146). What is governed is where the date LANDS, never which direction
        // it moved, so 14 Nov -> 12 Nov is fine while both are ahead. An UNCHANGED date always
        // passes, which is what keeps renaming / re-counting a trip that already started
        // working under this full-replace PUT. Same floor and same reasoning as
        // RetimeStopToHourHandler.cs:36-41 — one day of slack keeps a legitimate viewer-local
        // "today" that is still UTC-yesterday from being falsely rejected (MenuNest is Thai-first
        // at UTC+7, but it is a *travel* app).
        if (c.StartDate != trip.StartDate &&
            c.StartDate < DateOnly.FromDateTime(_clock.UtcNow).AddDays(-1))
            throw new DomainException("Cannot move a trip to a start date that is already in the past.");

        trip.UpdateDetails(c.Name, c.Destination, c.DefaultTravelMode);
        trip.Reschedule(c.StartDate, c.DayCount);

        var days = await _db.ItineraryDays
            .Where(d => d.TripId == trip.Id)
            .OrderBy(d => d.Date)
            .ToListAsync(ct);

        // Days beyond the new count are about to be removed, and their Stops cascade with them
        // (Stop->ItineraryDay FK, ON DELETE CASCADE — StopConfiguration.cs:22). Stop has no soft
        // delete and there is no restore endpoint, so that loss is unrecoverable: refuse unless
        // the caller has explicitly confirmed it (ADR-140). The SPA sets the flag only after the
        // user confirms (ADR-138); MCP exposes it so the agent path stops being silent. A shrink
        // over EMPTY days is an ordinary edit and passes without ceremony.
        var dropped = days.Skip(c.DayCount).ToList();
        if (dropped.Count > 0 && !c.AllowStopLoss)
        {
            var droppedIds = dropped.Select(d => d.Id).ToList();
            var atRisk = await _db.Stops.CountAsync(s => droppedIds.Contains(s.ItineraryDayId), ct);
            if (atRisk > 0)
                throw new DomainException(
                    $"Shrinking this trip to {c.DayCount} day(s) would delete {atRisk} scheduled stop(s) " +
                    $"on day(s) {c.DayCount + 1}-{days.Count} " +
                    $"({dropped[0].Date:yyyy-MM-dd} to {dropped[^1].Date:yyyy-MM-dd}). " +
                    "Re-send with allowStopLoss = true to confirm.");
        }

        // Add missing trailing days
        for (var i = days.Count; i < c.DayCount; i++)
            _db.ItineraryDays.Add(ItineraryDay.Create(trip.Id, c.StartDate.AddDays(i)));

        // Remove surplus trailing days. Stops on a dropped day cascade-delete (Stop→Day FK).
        // This is intentional per ADR-009's "add/remove trailing days", but it is silent data
        // loss: when an edit-trip UI is built, it must confirm before shrinking a trip that has
        // scheduled stops on the days being removed.
        foreach (var extra in days.Skip(c.DayCount))
            _db.ItineraryDays.Remove(extra);

        // Realign kept days' dates to the new start date
        var kept = Math.Min(days.Count, c.DayCount);
        DayRealigner.RealignDays(days.Take(kept).ToList(), c.StartDate);

        await _db.SaveChangesAsync(ct);
        return new TripDto(trip.Id, trip.Name, trip.Destination, trip.StartDate, trip.DayCount, trip.DefaultTravelMode, trip.IsDaily);
    }
}
