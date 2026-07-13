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
}