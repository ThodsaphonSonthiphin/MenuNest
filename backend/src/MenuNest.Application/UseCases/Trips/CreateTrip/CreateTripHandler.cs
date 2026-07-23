using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UseCases.Trips.CreateTrip;

public sealed class CreateTripHandler : ICommandHandler<CreateTripCommand, TripDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<CreateTripCommand> _validator;

    public CreateTripHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<CreateTripCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<TripDto> Handle(CreateTripCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);

        var trip = Trip.Create(user.Id, c.Name, c.StartDate, c.DayCount, c.DefaultTravelMode, c.Destination, c.IsDaily);
        _db.Trips.Add(trip);
        for (var i = 0; i < c.DayCount; i++)
        {
            var day = ItineraryDay.Create(trip.Id, c.StartDate.AddDays(i));
            if (c.IsDaily) day.SetUseCurrentTimeAsStart(true); // single day -> evergreen (ADR-132)
            _db.ItineraryDays.Add(day);
        }

        await _db.SaveChangesAsync(ct);
        return new TripDto(trip.Id, trip.Name, trip.Destination, trip.StartDate, trip.DayCount, trip.DefaultTravelMode, trip.IsDaily);
    }
}
