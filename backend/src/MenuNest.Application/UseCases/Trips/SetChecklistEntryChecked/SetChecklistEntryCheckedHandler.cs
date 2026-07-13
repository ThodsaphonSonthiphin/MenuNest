using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.SetChecklistEntryChecked;

public sealed class SetChecklistEntryCheckedHandler : ICommandHandler<SetChecklistEntryCheckedCommand, PlaceChecklistEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    public SetChecklistEntryCheckedHandler(IApplicationDbContext db, IUserProvisioner users) { _db = db; _users = users; }

    public async ValueTask<PlaceChecklistEntryDto> Handle(SetChecklistEntryCheckedCommand c, CancellationToken ct)
    {
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");
        var placeExists = await _db.TripPlaces.AnyAsync(p => p.Id == c.PlaceId && p.TripId == c.TripId, ct);
        if (!placeExists) throw new DomainException("Place not found.");

        var entry = await _db.PlaceChecklistEntries.FirstOrDefaultAsync(e => e.Id == c.EntryId && e.TripPlaceId == c.PlaceId, ct)
            ?? throw new DomainException("Checklist entry not found.");
        entry.SetChecked(c.IsChecked);
        await _db.SaveChangesAsync(ct);

        var name = await _db.ChecklistItems.Where(i => i.Id == entry.ChecklistItemId).Select(i => i.Name).FirstAsync(ct);
        return new PlaceChecklistEntryDto(entry.Id, entry.ChecklistItemId, name, entry.IsChecked);
    }
}