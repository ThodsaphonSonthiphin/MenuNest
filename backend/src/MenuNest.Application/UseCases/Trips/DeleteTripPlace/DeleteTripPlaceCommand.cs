using Mediator;
namespace MenuNest.Application.UseCases.Trips.DeleteTripPlace;
// ADR-167: Cascade defaults to FALSE so every existing caller — TripsController, the MCP
// TripTools delete, and the trips page's unconfirmed "เอาออกจากทริปนี้" button — keeps today's
// refusal. Only a caller that has shown a confirmation opts in.
public sealed record DeleteTripPlaceCommand(Guid TripId, Guid PlaceId, bool Cascade = false) : ICommand<Unit>;
