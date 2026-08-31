using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Monthly.CoverOverspending;

/// <summary>
/// Functionally identical to <c>MoveMoneyHandler</c> — decrements the source
/// envelope and increments the overspent envelope. Exists as a separate command
/// so the UI and audit log can distinguish deliberate "cover overspending"
/// from a general move between categories.
///
/// <para>menunest-215: a NULL <c>FromCategoryId</c> covers from Ready to Assign
/// instead. That figure is derived (<c>sum(accounts) − sum(envelope.available)</c>
/// in <c>GetMonthlySummaryHandler</c>), so there is no second row to decrement —
/// the overspent envelope is incremented alone and the derived figure falls by
/// the same amount. The act is recorded as an <c>Assign</c>, not a <c>Cover</c>:
/// a Cover row carries a source envelope in <c>CategoryId</c> and its
/// destination in <c>SecondCategoryId</c>, and <c>BudgetChangeApplier</c>
/// refuses one whose destination is null. An Assign is not a lossy substitute
/// but the accurate record — moving money out of Ready to Assign into one
/// envelope IS an assign, indistinguishable in effect from typing the figure
/// into that envelope's Assigned box, and it undoes through the existing
/// single-envelope delta branch with no new change kind and no migration.</para>
/// </summary>
public sealed class CoverOverspendingHandler : ICommandHandler<CoverOverspendingCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<CoverOverspendingCommand> _validator;
    private readonly AllowanceFreezer _freezer;
    private readonly IClock _clock;
    private readonly BudgetChangeRecorder _recorder;

    public CoverOverspendingHandler(
        IApplicationDbContext db,
        IUserProvisioner users,
        IValidator<CoverOverspendingCommand> validator,
        AllowanceFreezer freezer,
        IClock clock,
        BudgetChangeRecorder recorder)
    { _db = db; _users = users; _validator = validator; _freezer = freezer; _clock = clock; _recorder = recorder; }

    public async ValueTask<Unit> Handle(CoverOverspendingCommand cmd, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(cmd, ct);
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        // The source is resolved FIRST when there is one, so a from-category
        // that does not belong to this Family still throws before anything has
        // been added to the change tracker — the order this handler has always
        // had.
        var from = cmd.FromCategoryId is { } fromCategoryId
            ? await GetOrCreateAsync(familyId, fromCategoryId, cmd.Year, cmd.Month, ct)
            : null;
        var overspent = await GetOrCreateAsync(familyId, cmd.OverspentCategoryId, cmd.Year, cmd.Month, ct);

        overspent.AdjustAmount(+cmd.Amount);
        if (from is not null)
        {
            from.AdjustAmount(-cmd.Amount);
            _recorder.Record(BudgetChange.RecordMove(
                familyId, user.Id, cmd.Year, cmd.Month,
                cmd.FromCategoryId!.Value, cmd.OverspentCategoryId, cmd.Amount, isCover: true));
        }
        else
        {
            // menunest-215/193: the DELTA, so a concurrent assign by another
            // Family member survives both this write and its undo. Deliberately
            // not SetAssignedAmount with a client-computed absolute — that
            // clobbers the other member's figure.
            _recorder.Record(BudgetChange.RecordAssign(
                familyId, user.Id, cmd.Year, cmd.Month,
                cmd.OverspentCategoryId, +cmd.Amount, batchId: null));
        }

        await _db.SaveChangesAsync(ct);

        // menunest-181/189: only re-freeze when an everyday envelope is actually
        // involved — covering overspending between two non-everyday envelopes
        // is not a Budgeting event for this purpose, and so never needs the
        // viewer's time zone either (only resolved here, where it's actually used).
        // menunest-215: a cover from Ready to Assign has no source envelope, so
        // only the overspent one can make this a Budgeting event. The ids are
        // collected into a list rather than left as a `c.Id == cmd.FromCategoryId`
        // comparison against a null Guid? — that reads as false on every
        // provider today, but only by the accident of NULL-comparison
        // semantics, and nothing in the test suite would catch it changing.
        var involved = cmd.FromCategoryId is { } fromId
            ? new[] { fromId, cmd.OverspentCategoryId }
            : new[] { cmd.OverspentCategoryId };
        var touchesEveryday = await _db.BudgetCategories.AnyAsync(
            c => c.FamilyId == familyId && c.IsEveryday && involved.Contains(c.Id), ct);
        if (touchesEveryday)
        {
            var tz = BudgetTimeZone.Resolve(cmd.TimeZoneId);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz));
            var refrozen = await _freezer.RefreezeAsync(familyId, today, ct);
            if (refrozen is not null) await _db.SaveChangesAsync(ct);
        }

        return Unit.Value;
    }

    private async Task<MonthlyAssignment> GetOrCreateAsync(
        Guid familyId, Guid categoryId, int year, int month, CancellationToken ct)
    {
        var row = await _db.MonthlyAssignments.FirstOrDefaultAsync(
            x => x.FamilyId == familyId && x.CategoryId == categoryId
              && x.Year == year && x.Month == month, ct);
        if (row is not null) return row;

        var belongs = await _db.BudgetCategories.AnyAsync(
            c => c.Id == categoryId && c.FamilyId == familyId, ct);
        if (!belongs) throw new DomainException("Category not found.");

        var created = MonthlyAssignment.Create(familyId, categoryId, year, month, 0m);
        _db.MonthlyAssignments.Add(created);
        return created;
    }
}
