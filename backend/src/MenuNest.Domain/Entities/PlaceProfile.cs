using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A User-scoped, reusable MASTER record of the user's own enrichment for one Google place —
/// best-time window, review links, and (via PlaceProfileChecklistItem) a checklist item-set.
/// Keyed by (UserId, GooglePlaceId). Seeds a TripPlace on capture; per-trip edits do not change
/// it unless explicitly pushed (ADR-063/064). Holds no per-trip state (no checked flag).
/// </summary>
public sealed class PlaceProfile : Entity
{
    public Guid UserId { get; private set; }
    public string GooglePlaceId { get; private set; } = null!;
    public TimeOnly? BestTimeStart { get; private set; }
    public TimeOnly? BestTimeEnd { get; private set; }
    public string? Notes { get; private set; }

    private readonly List<ReviewLink> _reviewLinks = new();
    public IReadOnlyList<ReviewLink> ReviewLinks => _reviewLinks;

    private readonly List<SeasonPeriod> _seasonPeriods = new();
    public IReadOnlyList<SeasonPeriod> SeasonPeriods => _seasonPeriods;

    private PlaceProfile() { } // EF

    public static PlaceProfile Create(Guid userId, string googlePlaceId)
    {
        if (userId == Guid.Empty) throw new DomainException("UserId is required for a place profile.");
        if (string.IsNullOrWhiteSpace(googlePlaceId)) throw new DomainException("GooglePlaceId is required for a place profile.");
        return new PlaceProfile { UserId = userId, GooglePlaceId = googlePlaceId.Trim() };
    }

    public void SetBestTime(TimeOnly? start, TimeOnly? end)
    {
        if (start is null || end is null) { start = null; end = null; }
        else if (end <= start) throw new DomainException("Best-time end must be after start.");
        BestTimeStart = start;
        BestTimeEnd = end;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetNotes(string? notes)
    {
        var n = notes?.Trim();
        if (n is { Length: > 2000 }) throw new DomainException("Place note is too long (max 2000).");
        Notes = string.IsNullOrEmpty(n) ? null : n;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetReviewLinks(IEnumerable<ReviewLink> links)
    {
        var list = (links ?? Enumerable.Empty<ReviewLink>()).ToList();
        if (list.Count > 10) throw new DomainException("A place profile can have at most 10 review links.");
        _reviewLinks.Clear();
        _reviewLinks.AddRange(list);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetSeasonPeriods(IEnumerable<SeasonPeriod> periods)
    {
        var list = (periods ?? Enumerable.Empty<SeasonPeriod>()).ToList();
        if (list.Count > 12) throw new DomainException("A place profile can have at most 12 season periods.");
        _seasonPeriods.Clear();
        _seasonPeriods.AddRange(list);
        UpdatedAt = DateTime.UtcNow;
    }
}
