using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.DeleteWritingEntry;

public sealed class DeleteWritingEntryHandler : ICommandHandler<DeleteWritingEntryCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public DeleteWritingEntryHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(DeleteWritingEntryCommand command, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var entry = await _db.WritingEntries
            .FirstOrDefaultAsync(w => w.Id == command.Id && w.UserId == user.Id && w.DeletedAt == null, ct)
            ?? throw new DomainException("Writing entry not found.");

        entry.SoftDelete();
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
