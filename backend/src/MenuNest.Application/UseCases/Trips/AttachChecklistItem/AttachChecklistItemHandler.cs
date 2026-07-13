using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.AttachChecklistItem;

public sealed class AttachChecklistItemHandler : ICommandHandler<AttachChecklistItemCommand, PlaceChecklistEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<AttachChecklistItemCommand> _validator;
    public AttachChecklistItemHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<AttachChecklistItemCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<PlaceChecklistEntryDto> Handle(AttachChecklistItemCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");
        var place = await _db.TripPlaces.FirstOrDefaultAsync(p => p.Id == c.PlaceId && p.TripId == c.TripId, ct)
            ?? throw new DomainException("Place not found.");

        var name = ChecklistItem.NormalizeName(c.Name);
        var normalized = name.ToLowerInvariant();
        // Reuse by case-insensitive name (LOWER translates on both SQL Server and SQLite);
        // the (UserId, Name) unique index is the race backstop.
        var item = await _db.ChecklistItems.FirstOrDefaultAsync(i => i.UserId == user.Id && i.Name.ToLower() == normalized, ct);
        if (item is null)
        {
            item = ChecklistItem.Create(user.Id, name);
            _db.ChecklistItems.Add(item);
        }

        var entry = await _db.PlaceChecklistEntries
            .FirstOrDefaultAsync(e => e.TripPlaceId == c.PlaceId && e.ChecklistItemId == item.Id, ct);
        if (entry is null)
        {
            var count = await _db.PlaceChecklistEntries.CountAsync(e => e.TripPlaceId == c.PlaceId, ct);
            if (count >= PlaceChecklistEntry.MaxPerPlace)
                throw new DomainException($"A place can have at most {PlaceChecklistEntry.MaxPerPlace} checklist items.");
            entry = PlaceChecklistEntry.Create(c.PlaceId, item.Id);
            _db.PlaceChecklistEntries.Add(entry);
        }

        await _db.SaveChangesAsync(ct);
        // First-enrichment auto-create: attaching the first item to a place with no master yet
        // creates the master (including this just-persisted item). No-op once a master exists.
        if (await PlaceProfileSync.EnsureCreatedAsync(_db, user.Id, place, ct))
            await _db.SaveChangesAsync(ct);
        return new PlaceChecklistEntryDto(entry.Id, item.Id, item.Name, entry.IsChecked);
    }
}