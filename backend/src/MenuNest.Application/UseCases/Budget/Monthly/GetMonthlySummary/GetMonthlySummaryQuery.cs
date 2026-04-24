using Mediator;

namespace MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;

public sealed record GetMonthlySummaryQuery(int Year, int Month) : IQuery<MonthlySummaryDto>;
