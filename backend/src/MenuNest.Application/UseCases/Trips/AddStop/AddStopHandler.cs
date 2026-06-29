using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.AddStop;

public sealed class AddStopHandler : ICommandHandler<AddStopCommand, StopDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<AddStopCommand> _validator;

    public AddStopHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<AddStopCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<StopDto> Handle(AddStopCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var day = await _db.ItineraryDays.FirstOrDefaultAsync(d => d.Id == c.DayId
            && _db.Trips.Any(t => t.Id == d.TripId && t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null), ct)
            ?? throw new DomainException("Itinerary day not found.");
        var placeOk = await _db.TripPlaces.AnyAsync(p => p.Id == c.TripPlaceId && p.TripId == c.TripId, ct);
        if (!placeOk) throw new DomainException("Place not found in this trip.");

        var nextSeq = await _db.Stops.Where(s => s.ItineraryDayId == day.Id).CountAsync(ct);
        var stop = Stop.Create(day.Id, c.TripPlaceId, nextSeq, c.DwellMinutes, c.TravelModeToReach);
        _db.Stops.Add(stop);
        await _db.SaveChangesAsync(ct);
        return new StopDto(stop.Id, stop.TripPlaceId, stop.Sequence, stop.DwellMinutes, stop.TravelModeToReach, null);
    }
}
