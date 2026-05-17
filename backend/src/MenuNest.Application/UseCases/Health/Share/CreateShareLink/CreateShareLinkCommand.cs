using Mediator;

namespace MenuNest.Application.UseCases.Health.Share.CreateShareLink;

/// <summary>
/// Mints a new doctor-report share link covering the given date range,
/// valid for <see cref="ValidForDays"/> days from now. The raw token is
/// returned ONCE in the response; only its SHA-256 hash is persisted.
/// </summary>
public sealed record CreateShareLinkCommand(
    DateOnly DateFrom,
    DateOnly DateTo,
    int ValidForDays = 30) : ICommand<CreateShareLinkResultDto>;
