using Mediator;

namespace MenuNest.Application.UseCases.Stock.ListStock;

public sealed record ListStockQuery : IQuery<IReadOnlyList<StockItemDto>>;
