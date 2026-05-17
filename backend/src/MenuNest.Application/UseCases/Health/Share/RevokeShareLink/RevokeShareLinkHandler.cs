using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Share.RevokeShareLink;

public sealed class RevokeShareLinkHandler : ICommandHandler<RevokeShareLinkCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public RevokeShareLinkHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(RevokeShareLinkCommand command, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        // Single query + ownership check — guards against revoking another
        // user's link by id-guessing.
        var link = await _db.ShareLinks
            .FirstOrDefaultAsync(l => l.Id == command.ShareLinkId, ct)
            ?? throw new DomainException("Share link not found.");

        if (link.UserId != user.Id)
            throw new DomainException("Share link not found.");

        link.Revoke();
        await _db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
