using Mediator;

namespace MenuNest.Application.UseCases.Stock.DeleteStock;

public sealed record DeleteStockCommand(Guid Id) : ICommand;
