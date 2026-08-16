using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.ListWritingEntries;

public sealed class ListWritingEntriesHandler
    : IQueryHandler<ListWritingEntriesQuery, IReadOnlyList<WritingEntryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListWritingEntriesHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<WritingEntryDto>> Handle(
        ListWritingEntriesQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        return await _db.WritingEntries
            .Where(w => w.UserId == user.Id && w.DeletedAt == null)
            .OrderByDescending(w => w.Date)
            .Select(w => new WritingEntryDto(
                w.Id, w.Date, w.Text, w.ElapsedSeconds, w.WordsPerMinute, w.CorrectedAt, w.CreatedAt))
            .ToListAsync(ct);
    }
}
