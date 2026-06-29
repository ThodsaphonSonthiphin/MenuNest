using Mediator;
namespace MenuNest.Application.UseCases.Trips.ResolvePlace;
public sealed record ResolvePlaceCommand(string Url) : ICommand<ResolvedPlaceDto>;
