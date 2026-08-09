using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips.AttachChecklistItem;

public sealed class AttachChecklistItemHandler : ICommandHandler<AttachChecklistItemCommand, StopChecklistEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<AttachChecklistItemCommand> _validator;
    public AttachChecklistItemHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<AttachChecklistItemCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<StopChecklistEntryDto> Handle(AttachChecklistItemCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");
        var stopExists = await _db.Stops.AnyAsync(s => s.Id == c.StopId && _db.ItineraryDays.Any(d => d.Id == s.ItineraryDayId && d.TripId == c.TripId), ct);
        if (!stopExists) throw new DomainException("Stop not found.");

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

        var entry = await _db.StopChecklistEntries
            .FirstOrDefaultAsync(e => e.StopId == c.StopId && e.ChecklistItemId == item.Id, ct);
        if (entry is null)
        {
            var count = await _db.StopChecklistEntries.CountAsync(e => e.StopId == c.StopId, ct);
            if (count >= StopChecklistEntry.MaxPerStop)
                throw new DomainException($"A stop can have at most {StopChecklistEntry.MaxPerStop} checklist items.");
            entry = StopChecklistEntry.Create(c.StopId, item.Id);
            _db.StopChecklistEntries.Add(entry);
        }

        // NOTE: master-profile auto-create is deliberately NOT wired here (ADR-064, post-scrutinize):
        // it stays on the editor Save (UpdateTripPlace) + explicit push, so the existing #23 checklist
        // path keeps its single write and gains no side effects. A later Save captures the item-set.
        await _db.SaveChangesAsync(ct);
        return new StopChecklistEntryDto(entry.Id, item.Id, item.Name, entry.IsChecked);
    }
}