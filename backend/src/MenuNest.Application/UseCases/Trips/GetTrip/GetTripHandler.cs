using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.GetTrip;

public sealed class GetTripHandler : IQueryHandler<GetTripQuery, TripDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;

    public GetTripHandler(IApplicationDbContext db, IUserProvisioner users) { _db = db; _users = users; }

    public async ValueTask<TripDto> Handle(GetTripQuery q, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var trip = await _db.Trips
            .Where(t => t.Id == q.TripId && t.UserId == user.Id && t.DeletedAt == null)
            .Select(t => new TripDto(t.Id, t.Name, t.Destination, t.StartDate, t.DayCount, t.DefaultTravelMode, t.IsDaily))
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("Trip not found.");
        return trip;
    }
}
