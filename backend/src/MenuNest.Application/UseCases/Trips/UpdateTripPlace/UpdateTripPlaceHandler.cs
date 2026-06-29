using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
namespace MenuNest.Application.UseCases.Trips.UpdateTripPlace;

public sealed class UpdateTripPlaceHandler : ICommandHandler<UpdateTripPlaceCommand, TripPlaceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<UpdateTripPlaceCommand> _validator;
    public UpdateTripPlaceHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<UpdateTripPlaceCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<TripPlaceDto> Handle(UpdateTripPlaceCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        var user = await _users.GetOrProvisionCurrentAsync(ct);
        var owns = await _db.Trips.AnyAsync(t => t.Id == c.TripId && t.UserId == user.Id && t.DeletedAt == null, ct);
        if (!owns) throw new DomainException("Trip not found.");

        var place = await _db.TripPlaces.FirstOrDefaultAsync(p => p.Id == c.PlaceId && p.TripId == c.TripId, ct)
            ?? throw new DomainException("Place not found.");

        place.UpdateDetails(c.Name, c.Category, c.Address, c.FeeNote, c.Notes);
        place.SetBestTime(c.BestTimeStart, c.BestTimeEnd);

        await _db.SaveChangesAsync(ct);
        return AddTripPlaceHandler.ToDto(place);
    }
}
