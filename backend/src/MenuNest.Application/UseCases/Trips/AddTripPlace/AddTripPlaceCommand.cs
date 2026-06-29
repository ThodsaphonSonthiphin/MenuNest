using Mediator;
using MenuNest.Domain.Enums;
namespace MenuNest.Application.UseCases.Trips.AddTripPlace;
public sealed record AddTripPlaceCommand(
    Guid TripId, string Name, double Lat, double Lng, PlaceCategory Category,
    string? GooglePlaceId, string? Address, int? PriceLevel, string? PhotoUrl, string? OpeningHoursJson)
    : ICommand<TripPlaceDto>;
