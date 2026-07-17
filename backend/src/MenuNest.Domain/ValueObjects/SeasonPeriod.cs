using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.ValueObjects;

/// <summary>
/// A per-Place season period (issue #19): a set of calendar months (0..11) marked good-to-visit
/// or to-avoid, with an optional reason. Positional record with a public ctor so System.Text.Json
/// can round-trip it from the JSON column; user input is validated through <see cref="Create"/>.
/// </summary>
public sealed record SeasonPeriod(SeasonKind Kind, IReadOnlyList<int> Months, string? Note)
{
    public static SeasonPeriod Create(SeasonKind kind, IEnumerable<int>? months, string? note)
    {
        var m = (months ?? Enumerable.Empty<int>())
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        if (m.Count == 0) throw new DomainException("A season period must include at least one month.");
        if (m.Any(x => x is < 0 or > 11)) throw new DomainException("Season months must be 0–11 (Jan–Dec).");
        var n = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (n is { Length: > 200 }) throw new DomainException("Season note is too long (max 200).");
        return new SeasonPeriod(kind, m, n);
    }
}
