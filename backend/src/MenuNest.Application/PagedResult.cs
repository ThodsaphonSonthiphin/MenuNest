namespace MenuNest.Application;

public sealed record PagedResult<T>(IReadOnlyList<T> Result, int Count);
