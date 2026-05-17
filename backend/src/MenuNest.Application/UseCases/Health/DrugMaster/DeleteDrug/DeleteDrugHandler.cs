using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.DrugMaster.DeleteDrug;

public sealed class DeleteDrugHandler : ICommandHandler<DeleteDrugCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public DeleteDrugHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(DeleteDrugCommand command, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var drug = await _db.Drugs
            .FirstOrDefaultAsync(d => d.Id == command.Id && d.UserId == user.Id && d.DeletedAt == null, ct)
            ?? throw new DomainException("Drug not found.");

        // Soft delete so historical Intakes keep their drug reference.
        drug.SoftDelete();

        // Cascade soft-delete attached photos so they no longer show up in
        // the drug photo list. The blobs themselves stay until a future
        // cleanup job; that's acceptable for Phase 1.
        var photos = await _db.Photos
            .Where(p => p.ParentType == PhotoParentType.Drug && p.ParentId == drug.Id && p.DeletedAt == null)
            .ToListAsync(ct);

        foreach (var photo in photos)
        {
            photo.SoftDelete();
        }

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
