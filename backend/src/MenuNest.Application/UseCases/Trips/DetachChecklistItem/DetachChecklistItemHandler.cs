using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.DetachChecklistItem;

public sealed class DetachChecklistItemHandler : ICommandHandler<DetachChecklistItemCommand, bool>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public DetachChecklistItemHandler(IApplicationDbContext db, IUserProvisioner users) { _db = db; _users = users; }

    public async ValueTask<bool> Handle(DetachChecklistItemCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");
        var placeExists = await _db.TripPlaces.AnyAsync(p => p.Id == c.PlaceId && p.TripId == c.TripId, ct);
        if (!placeExists) throw new DomainException("Place not found.");

        var entry = await _db.PlaceChecklistEntries.FirstOrDefaultAsync(e => e.Id == c.EntryId && e.TripPlaceId == c.PlaceId, ct)
            ?? throw new DomainException("Checklist entry not found.");
        _db.PlaceChecklistEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}