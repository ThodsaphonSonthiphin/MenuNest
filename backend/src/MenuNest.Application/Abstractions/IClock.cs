namespace MenuNest.Application.Abstractions;

/// <summary>
/// Time abstraction so handlers can be tested deterministically.
/// Production binds to <c>SystemClock</c> (returns <c>DateTime.UtcNow</c>);
/// tests bind to <c>FixedClock</c>.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
