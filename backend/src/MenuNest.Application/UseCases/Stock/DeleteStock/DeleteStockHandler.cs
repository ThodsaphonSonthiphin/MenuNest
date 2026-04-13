using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Stock.DeleteStock;

public sealed class DeleteStockHandler : ICommandHandler<DeleteStockCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public DeleteStockHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(DeleteStockCommand command, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var stock = await _db.StockItems
            .FirstOrDefaultAsync(s => s.Id == command.Id && s.FamilyId == familyId, ct)
            ?? throw new DomainException("Stock entry not found.");

        _db.StockItems.Remove(stock);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
