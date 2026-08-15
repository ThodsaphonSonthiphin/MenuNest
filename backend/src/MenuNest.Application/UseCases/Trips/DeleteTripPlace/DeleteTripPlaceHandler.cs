using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
namespace MenuNest.Application.UseCases.Trips.DeleteTripPlace;

public sealed class DeleteTripPlaceHandler : ICommandHandler<DeleteTripPlaceCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public DeleteTripPlaceHandler(IApplicationDbContext db, IUserProvisioner users)
    { _db = db; _users = users; }

    public async ValueTask<Unit> Handle(DeleteTripPlaceCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");

        var place = await _db.TripPlaces.FirstOrDefaultAsync(p => p.Id == c.PlaceId && p.TripId == c.TripId, ct)
            ?? throw new DomainException("Place not found.");

        var scheduled = await _db.Stops
            .Where(s => s.TripPlaceId == c.PlaceId
                     && _db.ItineraryDays.Any(d => d.Id == s.ItineraryDayId && d.TripId == c.TripId))
            .ToListAsync(ct);

        if (scheduled.Count > 0)
        {
            if (!c.Cascade)
                throw new DomainException("ลบไม่ได้ — สถานที่นี้ถูกจัดลงตารางแล้ว ลบจุดในแผนก่อน");

            var removedIds = scheduled.Select(s => s.Id).ToList();
            _db.Stops.RemoveRange(scheduled);

            // Close the gaps, one day at a time — a Place can be scheduled across several days,
            // so this is RemoveStopHandler:27-33's invariant applied per affected day. The rows
            // are only marked Deleted until SaveChanges, so they must be filtered out by id.
            foreach (var dayId in scheduled.Select(s => s.ItineraryDayId).Distinct())
            {
                var remaining = await _db.Stops
                    .Where(s => s.ItineraryDayId == dayId && !removedIds.Contains(s.Id))
                    .OrderBy(s => s.Sequence)
                    .ToListAsync(ct);
                for (var i = 0; i < remaining.Count; i++) remaining[i].SetSequence(i);
            }
        }

        // Stop → TripPlace is DeleteBehavior.NoAction (StopConfiguration.cs:23) — there is no
        // database cascade, so the Stops above and this row must land in ONE SaveChanges, with
        // EF ordering the dependents first.
        _db.TripPlaces.Remove(place);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
