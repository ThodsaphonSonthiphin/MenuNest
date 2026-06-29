using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
namespace MenuNest.Application.UseCases.Trips.ResolvePlace;

public sealed class ResolvePlaceHandler : ICommandHandler<ResolvePlaceCommand, ResolvedPlaceDto>
{
    private readonly IPlaceResolver _resolver;
    private readonly IUserProvisioner _users;
    private readonly IValidator<ResolvePlaceCommand> _validator;
    public ResolvePlaceHandler(IPlaceResolver resolver, IUserProvisioner users, IValidator<ResolvePlaceCommand> validator)
    { _resolver = resolver; _users = users; _validator = validator; }

    public async ValueTask<ResolvedPlaceDto> Handle(ResolvePlaceCommand c, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(c, ct);
        await _users.GetOrProvisionCurrentAsync(ct); // ensure authenticated
        return await _resolver.ResolveFromUrlAsync(c.Url, ct);
    }
}
