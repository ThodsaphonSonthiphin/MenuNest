using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UseCases.Health.Share.CreateShareLink;

/// <summary>
/// Issues a new share-link token + persists the matching <see cref="ShareLink"/>
/// row. The raw token is returned exactly once in the response so the
/// frontend can render it as a QR code; only the SHA-256 hash is stored,
/// so a DB leak cannot recover live tokens.
/// </summary>
public sealed class CreateShareLinkHandler : ICommandHandler<CreateShareLinkCommand, CreateShareLinkResultDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IShareTokenService _shareTokens;
    private readonly IShareUrlBuilder _urlBuilder;
    private readonly IClock _clock;
    private readonly IValidator<CreateShareLinkCommand> _validator;

    public CreateShareLinkHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IShareTokenService shareTokens,
        IShareUrlBuilder urlBuilder,
        IClock clock,
        IValidator<CreateShareLinkCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _shareTokens = shareTokens;
        _urlBuilder = urlBuilder;
        _clock = clock;
        _validator = validator;
    }

    public async ValueTask<CreateShareLinkResultDto> Handle(
        CreateShareLinkCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var expiresAt = _clock.UtcNow.AddDays(command.ValidForDays);
        var issuance = _shareTokens.Issue(user.Id, command.DateFrom, command.DateTo, expiresAt);

        var link = ShareLink.Create(
            userId: user.Id,
            tokenHash: issuance.Hash,
            dateFrom: command.DateFrom,
            dateTo: command.DateTo,
            expiresAt: expiresAt);

        _db.ShareLinks.Add(link);
        await _db.SaveChangesAsync(ct);

        return new CreateShareLinkResultDto(
            Token: issuance.RawToken,
            ShareUrl: _urlBuilder.BuildShareUrl(issuance.RawToken),
            ShareId: link.Id,
            ExpiresAt: link.ExpiresAt,
            DateFrom: link.DateFrom,
            DateTo: link.DateTo);
    }
}
