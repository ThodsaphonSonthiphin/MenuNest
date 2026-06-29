using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
namespace MenuNest.Application.UseCases.Trips.ListTripPlaces;

public sealed class ListTripPlacesHandler : IQueryHandler<ListTripPlacesQuery, IReadOnlyList<TripPlaceDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public ListTripPlacesHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<IReadOnlyList<TripPlaceDto>> Handle(ListTripPlacesQuery c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");

        var places = await _db.TripPlaces
            .Where(p => p.TripId == c.TripId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        return places.Select(AddTripPlaceHandler.ToDto).ToList();
    }
}
