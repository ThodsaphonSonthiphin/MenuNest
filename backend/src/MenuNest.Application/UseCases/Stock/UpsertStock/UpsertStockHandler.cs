using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Stock.UpsertStock;

public sealed class UpsertStockHandler : ICommandHandler<UpsertStockCommand, StockItemDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<UpsertStockCommand> _validator;

    public UpsertStockHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<UpsertStockCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<StockItemDto> Handle(UpsertStockCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var ingredient = await _db.Ingredients
            .FirstOrDefaultAsync(i => i.Id == command.IngredientId && i.FamilyId == familyId, ct)
            ?? throw new DomainException("Ingredient not found in this family's master list.");

        var stock = await _db.StockItems
            .FirstOrDefaultAsync(s => s.FamilyId == familyId && s.IngredientId == ingredient.Id, ct);

        if (stock is null)
        {
            stock = StockItem.Create(familyId, ingredient.Id, command.Quantity, user.Id);
            _db.StockItems.Add(stock);
        }
        else
        {
            stock.SetQuantity(command.Quantity, user.Id);
        }

        await _db.SaveChangesAsync(ct);

        return new StockItemDto(
            stock.Id,
            ingredient.Id,
            ingredient.Name,
            ingredient.Unit,
            stock.Quantity,
            stock.UpdatedAt ?? stock.CreatedAt,
            stock.UpdatedByUserId);
    }
}
