using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Trips;

/// <summary>
/// Shared master-profile plumbing used by capture (seed), the editor Save / checklist attach
/// (auto-create), and push-to-master (upsert). None of these methods call SaveChanges — the
/// caller owns the unit of work. All are no-ops for a place with no GooglePlaceId (ADR-066).
/// </summary>
public static class PlaceProfileSync
{
    /// <summary>Copy an existing profile''s enrichment into a freshly-created (unsaved) TripPlace.
    /// Returns true iff a profile existed and was applied.</summary>
    public static async Task<bool> SeedIntoAsync(IApplicationDbContext db, Guid userId, TripPlace place, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(place.GooglePlaceId)) return false;
        var profile = await db.PlaceProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId && p.GooglePlaceId == place.GooglePlaceId, ct);
        if (profile is null) return false;

        place.SetBestTime(profile.BestTimeStart, profile.BestTimeEnd);
        place.SetReviewLinks(profile.ReviewLinks);
        place.SetSeasonPeriods(profile.SeasonPeriods);
        place.SetNotes(profile.Notes);
        var itemIds = await db.PlaceProfileChecklistItems
            .Where(x => x.PlaceProfileId == profile.Id)
            .Select(x => x.ChecklistItemId)
            .ToListAsync(ct);
        foreach (var itemId in itemIds)
            db.PlaceChecklistEntries.Add(PlaceChecklistEntry.Create(place.Id, itemId));
        return true;
    }

    /// <summary>Whether a master profile exists for this user + place_id.</summary>
    public static async Task<bool> ExistsAsync(IApplicationDbContext db, Guid userId, string? googlePlaceId, CancellationToken ct)
        => !string.IsNullOrEmpty(googlePlaceId)
           && await db.PlaceProfiles.AnyAsync(p => p.UserId == userId && p.GooglePlaceId == googlePlaceId, ct);

    /// <summary>Create the profile from the place''s CURRENT enrichment iff none exists yet
    /// (first-enrichment auto-create). Returns true iff a profile was created.</summary>
    public static async Task<bool> EnsureCreatedAsync(IApplicationDbContext db, Guid userId, TripPlace place, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(place.GooglePlaceId)) return false;
        var exists = await db.PlaceProfiles.AnyAsync(p => p.UserId == userId && p.GooglePlaceId == place.GooglePlaceId, ct);
        if (exists) return false;
        await UpsertFromAsync(db, userId, place, ct);
        return true;
    }

    /// <summary>Create-or-overwrite the profile from the place''s current best-time, review links,
    /// and checklist item-SET (push-to-master).</summary>
    public static async Task UpsertFromAsync(IApplicationDbContext db, Guid userId, TripPlace place, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(place.GooglePlaceId))
            throw new Domain.Exceptions.DomainException("This place has no Google place to save to your library.");

        var profile = await db.PlaceProfiles.FirstOrDefaultAsync(p => p.UserId == userId && p.GooglePlaceId == place.GooglePlaceId, ct);
        if (profile is null)
        {
            profile = PlaceProfile.Create(userId, place.GooglePlaceId);
            db.PlaceProfiles.Add(profile);
        }
        profile.SetBestTime(place.BestTimeStart, place.BestTimeEnd);
        profile.SetReviewLinks(place.ReviewLinks);
        profile.SetSeasonPeriods(place.SeasonPeriods);
        profile.SetNotes(place.Notes);

        var currentItemIds = await db.PlaceChecklistEntries
            .Where(e => e.TripPlaceId == place.Id).Select(e => e.ChecklistItemId).ToListAsync(ct);
        var links = await db.PlaceProfileChecklistItems.Where(x => x.PlaceProfileId == profile.Id).ToListAsync(ct);
        db.PlaceProfileChecklistItems.RemoveRange(links.Where(x => !currentItemIds.Contains(x.ChecklistItemId)));
        var have = links.Select(x => x.ChecklistItemId).ToHashSet();
        foreach (var id in currentItemIds.Where(id => !have.Contains(id)))
            db.PlaceProfileChecklistItems.Add(PlaceProfileChecklistItem.Create(profile.Id, id));
    }

    /// <summary>Overwrite ONLY the master's Notes + ReviewLinks from the place (write-through, ADR-103).
    /// No-op when the place has no GooglePlaceId or no master exists yet. Caller owns SaveChanges.</summary>
    public static async Task WriteThroughNotesAndLinksAsync(IApplicationDbContext db, Guid userId, TripPlace place, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(place.GooglePlaceId)) return;
        var profile = await db.PlaceProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId && p.GooglePlaceId == place.GooglePlaceId, ct);
        if (profile is null) return;
        profile.SetNotes(place.Notes);
        profile.SetReviewLinks(place.ReviewLinks);
    }
}