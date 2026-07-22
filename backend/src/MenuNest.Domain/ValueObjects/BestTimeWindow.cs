using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.ValueObjects;

/// <summary>
/// A per-Place "good" time-of-day window (issue #38): a start-end wall-clock range with an
/// optional reason. Positional record with a public ctor so System.Text.Json can round-trip it
/// from the JSON column; user input is validated through <see cref="Create"/>. Every window is
/// "good" - there is no avoid kind (that is Season's calendar axis). Mirrors SeasonPeriod.
/// </summary>
public sealed record BestTimeWindow(TimeOnly Start, TimeOnly End, string? Note)
{
    public static BestTimeWindow Create(TimeOnly start, TimeOnly end, string? note)
    {
        if (end <= start) throw new DomainException("Best-time end must be after start.");
        var n = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (n is { Length: > 200 }) throw new DomainException("Best-time note is too long (max 200).");
        return new BestTimeWindow(start, end, n);
    }
}