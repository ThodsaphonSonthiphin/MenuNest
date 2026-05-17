using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Share.ListMyShareLinks;

public sealed class ListMyShareLinksHandler
    : IQueryHandler<ListMyShareLinksQuery, IReadOnlyList<ShareLinkSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListMyShareLinksHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<ShareLinkSummaryDto>> Handle(
        ListMyShareLinksQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var rows = await _db.ShareLinks
            .Where(l => l.UserId == user.Id)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new ShareLinkSummaryDto(
                l.Id,
                l.DateFrom,
                l.DateTo,
                l.CreatedAt,
                l.ExpiresAt,
                l.RevokedAt,
                l.AccessCount,
                l.LastAccessedAt))
            .ToListAsync(ct);

        return rows;
    }
}
