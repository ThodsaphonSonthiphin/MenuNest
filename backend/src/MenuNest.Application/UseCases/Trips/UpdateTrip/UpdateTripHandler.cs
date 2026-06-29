using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.UpdateTrip;

public sealed class UpdateTripHandler : ICommandHandler<UpdateTripCommand, TripDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<UpdateTripCommand> _validator;

    public UpdateTripHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<UpdateTripCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<TripDto> Handle(UpdateTripCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var trip = await _db.Trips
            .FirstOrDefaultAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct)
            ?? throw new DomainException("Trip not found.");

        trip.UpdateDetails(c.Name, c.Destination, c.DefaultTravelMode);
        trip.Reschedule(c.StartDate, c.DayCount);

        var days = await _db.ItineraryDays
            .Where(d => d.TripId == trip.Id)
            .OrderBy(d => d.Date)
            .ToListAsync(ct);

        // Add missing trailing days
        for (var i = days.Count; i < c.DayCount; i++)
            _db.ItineraryDays.Add(ItineraryDay.Create(trip.Id, c.StartDate.AddDays(i)));

        // Remove surplus trailing days
        foreach (var extra in days.Skip(c.DayCount))
            _db.ItineraryDays.Remove(extra);

        // Realign kept days' dates to the new start date
        var kept = Math.Min(days.Count, c.DayCount);
        for (var i = 0; i < kept; i++)
            days[i].SetDate(c.StartDate.AddDays(i));

        await _db.SaveChangesAsync(ct);
        return new TripDto(trip.Id, trip.Name, trip.Destination, trip.StartDate, trip.DayCount, trip.DefaultTravelMode);
    }
}
