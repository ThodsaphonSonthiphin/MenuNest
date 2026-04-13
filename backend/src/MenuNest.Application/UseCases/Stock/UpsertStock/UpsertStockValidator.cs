using FluentValidation;

namespace MenuNest.Application.UseCases.Stock.UpsertStock;

public sealed class UpsertStockValidator : AbstractValidator<UpsertStockCommand>
{
    public UpsertStockValidator()
    {
        RuleFor(x => x.IngredientId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0m);
    }
}
