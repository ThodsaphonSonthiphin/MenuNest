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

        var place = TripPlace.Create(c.TripId, c.Name, c.Lat, c.Lng, c.Category,
            c.GooglePlaceId, c.Address, c.PriceLevel, c.PhotoUrl, c.OpeningHoursJson);
        _db.TripPlaces.Add(place);
        var seeded = await PlaceProfileSync.SeedIntoAsync(_db, user.Id, place, ct);
        await _db.SaveChangesAsync(ct);

        var checklist = seeded
            ? await (from e in _db.PlaceChecklistEntries
                     join i in _db.ChecklistItems on e.ChecklistItemId equals i.Id
                     where e.TripPlaceId == place.Id
                     orderby e.CreatedAt, e.Id
                     select new PlaceChecklistEntryDto(e.Id, e.ChecklistItemId, i.Name, e.IsChecked)).ToListAsync(ct)
            : (IReadOnlyList<PlaceChecklistEntryDto>)Array.Empty<PlaceChecklistEntryDto>();
        return ToDto(place, checklist, seeded);
    }

    internal static TripPlaceDto ToDto(TripPlace p) => ToDto(p, Array.Empty<PlaceChecklistEntryDto>(), false);

    internal static TripPlaceDto ToDto(TripPlace p, IReadOnlyList<PlaceChecklistEntryDto> checklist, bool hasProfile = false) => new(
        p.Id, p.TripId, p.GooglePlaceId, p.Name, p.Lat, p.Lng, p.Address, p.Category,
        p.PriceLevel, p.PhotoUrl, p.BestTimeStart, p.BestTimeEnd, p.OpeningHoursJson, p.FeeNote, p.Notes,
        p.ReviewLinks.Select(r => new ReviewLinkDto(r.Url, r.Label)).ToList(),
        checklist, hasProfile,
        p.SeasonPeriods.Select(s => new SeasonPeriodDto(s.Kind, s.Months.ToList(), s.Note)).ToList());
}