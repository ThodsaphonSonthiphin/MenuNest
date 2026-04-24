using Mediator;

namespace MenuNest.Application.UseCases.Budget.Monthly.SetMonthlyIncome;

public sealed record SetMonthlyIncomeCommand(int Year, int Month, decimal Amount) : ICommand<Unit>;
