using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.ListPendingWritingEntries;

public sealed class ListPendingWritingEntriesHandler
    : IQueryHandler<ListPendingWritingEntriesQuery, IReadOnlyList<PendingWritingEntryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListPendingWritingEntriesHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<PendingWritingEntryDto>> Handle(
        ListPendingWritingEntriesQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        return await _db.WritingEntries
            .Where(w => w.UserId == user.Id && w.DeletedAt == null && w.CorrectedAt == null)
            // Same ordering as ListWritingEntries, incl. the CreatedAt tie-break
            // added in 5b4b56d — two sittings on one date must be stably ordered.
            .OrderByDescending(w => w.Date)
            .ThenByDescending(w => w.CreatedAt)
            .Select(w => new PendingWritingEntryDto(
                w.Id, w.Date, w.Text, w.ElapsedSeconds, w.WordsPerMinute))
            .ToListAsync(ct);
    }
}
