using MenuNest.Application.Abstractions;

namespace MenuNest.Infrastructure.Services;

/// <summary>
/// Default <see cref="IClock"/> implementation — returns
/// <see cref="DateTime.UtcNow"/>. Tests substitute a fixed-time clock.
/// </summary>
internal sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
