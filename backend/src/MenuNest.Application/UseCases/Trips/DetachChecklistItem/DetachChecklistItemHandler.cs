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
        var stopExists = await _db.Stops.AnyAsync(s => s.Id == c.StopId && _db.ItineraryDays.Any(d => d.Id == s.ItineraryDayId && d.TripId == c.TripId), ct);
        if (!stopExists) throw new DomainException("Stop not found.");

        var entry = await _db.StopChecklistEntries.FirstOrDefaultAsync(e => e.Id == c.EntryId && e.StopId == c.StopId, ct)
            ?? throw new DomainException("Checklist entry not found.");
        _db.StopChecklistEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}