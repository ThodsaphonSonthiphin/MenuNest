using MenuNest.Application.Abstractions;

namespace MenuNest.Application.UnitTests.Support;

/// <summary>
/// Test <see cref="IClock"/> that returns a fixed <see cref="UtcNow"/>
/// value. Tests can mutate <see cref="UtcNow"/> to advance time
/// deterministically.
/// </summary>
public sealed class FixedClock : IClock
{
    public DateTime UtcNow { get; set; }

    public FixedClock(DateTime utcNow) => UtcNow = utcNow;
}
