using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
namespace MenuNest.Application.UseCases.Trips.AddTripPlace;

public sealed class AddTripPlaceHandler : ICommandHandler<AddTripPlaceCommand, TripPlaceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<AddTripPlaceCommand> _validator;
    public AddTripPlaceHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<AddTripPlaceCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<TripPlaceDto> Handle(AddTripPlaceCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");

        // ADR-149 §1: an exact place_id already on this Trip is idempotent -- return the existing
        // row rather than inserting a second one or letting the filtered unique index 500. The
        // policy lives here so the SPA, MCP and a race all get the same answer. Nothing is
        // merged: a capture is not an edit, and the user did not ask for one.
        if (!string.IsNullOrEmpty(c.GooglePlaceId))
        {
            var existing = await _db.TripPlaces
                .FirstOrDefaultAsync(p => p.TripId == c.TripId && p.GooglePlaceId == c.GooglePlaceId, ct);
            if (existing is not null)
            {
                var hasMaster = await PlaceProfileSync.ExistsAsync(_db, user.Id, c.GooglePlaceId, ct);
                return ToDto(existing, hasMaster);
            }
        }

        var place = TripPlace.Create(c.TripId, c.Name, c.Lat, c.Lng, c.Category,
            c.GooglePlaceId, c.Address, c.PriceLevel, c.PhotoUrl, c.OpeningHoursJson,
            c.OriginTripPlaceId);
        _db.TripPlaces.Add(place);
        var seeded = await PlaceProfileSync.SeedIntoAsync(_db, user.Id, place, ct);
        ApplyCopiedEnrichment(place, c);
        await _db.SaveChangesAsync(ct);

        return ToDto(place, seeded);
    }

    /// <summary>
    /// ADR-156 §4: the origin row's enrichment, copied at add-time -- applied PER FIELD, only
    /// to whatever the master (if any) left empty, so a master that actually holds a value for
    /// a field still wins for that field. `SeedIntoAsync` returning true only means a master
    /// ROW exists; BestTimeWindows/SeasonPeriods are push-only (UpdateTripPlaceHandler never
    /// writes them through), so an existing master is routinely empty for those two, and an
    /// all-or-nothing gate on "a master exists" would silently drop them. Mirrors what
    /// PlaceProfileSync.SeedIntoAsync already does for the master case.
    /// </summary>
    private static void ApplyCopiedEnrichment(TripPlace place, AddTripPlaceCommand c)
    {
        if (place.Notes is null && c.Notes is not null) place.SetNotes(c.Notes);
        if (place.ReviewLinks.Count == 0 && c.ReviewLinks is { Count: > 0 })
            place.SetReviewLinks(c.ReviewLinks.Select(r => ReviewLink.Create(r.Url, r.Label)));
        if (place.BestTimeWindows.Count == 0 && c.BestTimeWindows is { Count: > 0 })
            place.SetBestTimeWindows(c.BestTimeWindows.Select(w => BestTimeWindow.Create(w.Start, w.End, w.Note)));
        if (place.SeasonPeriods.Count == 0 && c.SeasonPeriods is { Count: > 0 })
            place.SetSeasonPeriods(c.SeasonPeriods.Select(s => SeasonPeriod.Create(s.Kind, s.Months, s.Note)));
    }

    internal static TripPlaceDto ToDto(TripPlace p, bool hasProfile = false) => new(
        p.Id, p.TripId, p.GooglePlaceId, p.Name, p.Lat, p.Lng, p.Address, p.Category,
        p.PriceLevel, p.PhotoUrl, p.OpeningHoursJson, p.FeeNote, p.Notes,
        p.ReviewLinks.Select(r => new ReviewLinkDto(r.Url, r.Label)).ToList(),
        hasProfile,
        p.SeasonPeriods.Select(s => new SeasonPeriodDto(s.Kind, s.Months.ToList(), s.Note)).ToList(),
        p.BestTimeWindows.Select(w => new BestTimeWindowDto(w.Start, w.End, w.Note)).ToList());
}