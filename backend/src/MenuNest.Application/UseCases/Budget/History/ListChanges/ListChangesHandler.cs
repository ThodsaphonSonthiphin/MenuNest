using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.History.ListChanges;

public sealed class ListChangesHandler : IQueryHandler<ListChangesQuery, IReadOnlyList<BudgetChangeDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IClock _clock;

    public ListChangesHandler(IApplicationDbContext db, IUserProvisioner users, IClock clock)
    { _db = db; _users = users; _clock = clock; }

    public async ValueTask<IReadOnlyList<BudgetChangeDto>> Handle(
        ListChangesQuery q, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        // menunest-194: min(7 days, since the 1st of the requested month). The
        // month is a HARD cut — a row from a previous month is never returned,
        // even when it is inside seven days — so the Year/Month equality below
        // does that half and this floor only trims within the month.
        var monthStart = new DateTime(q.Year, q.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var sevenDaysAgo = _clock.UtcNow.AddDays(-7);
        var floor = monthStart > sevenDaysAgo ? monthStart : sevenDaysAgo;

        var rows = await (
            from h in _db.BudgetChanges
            join u in _db.Users on h.UserId equals u.Id
            where h.FamilyId == familyId
               && h.Year == q.Year && h.Month == q.Month
               && h.CreatedAt >= floor
            orderby h.CreatedAt descending
            select new
            {
                h.Id, h.UserId, UserName = u.DisplayName, h.Kind, h.BatchId,
                h.CategoryId, h.SecondCategoryId, h.Delta, h.FlagValue,
                h.IsUndone, h.UndoneByUserId, h.CreatedAt
            }).ToListAsync(ct);

        var categoryIds = rows.Select(r => r.CategoryId)
            .Concat(rows.Where(r => r.SecondCategoryId != null).Select(r => r.SecondCategoryId!.Value))
            .Distinct().ToList();
        var categoryNames = await _db.BudgetCategories
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var undoerIds = rows.Where(r => r.UndoneByUserId != null)
            .Select(r => r.UndoneByUserId!.Value).Distinct().ToList();
        var undoerNames = await _db.Users
            .Where(u => undoerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return rows.Select(r =>
        {
            // menunest-197: a row whose Envelope is gone STAYS on the list,
            // unpressable, saying why — it is never dropped.
            var hasCategory = categoryNames.TryGetValue(r.CategoryId, out var categoryName);
            var secondName = r.SecondCategoryId is null
                ? null
                : categoryNames.TryGetValue(r.SecondCategoryId.Value, out var sn) ? sn : "(deleted envelope)";
            var gone = !hasCategory || secondName == "(deleted envelope)";

            return new BudgetChangeDto(
                r.Id, r.UserId, r.UserName, r.Kind, r.BatchId,
                hasCategory ? categoryName! : "(deleted envelope)",
                secondName,
                r.Delta, r.FlagValue, r.IsUndone,
                r.UndoneByUserId is null
                    ? null
                    : undoerNames.TryGetValue(r.UndoneByUserId.Value, out var un) ? un : null,
                r.CreatedAt,
                CanUndo: !gone,
                BlockedReason: gone ? "That envelope was deleted." : null);
        }).ToList();
    }
}
