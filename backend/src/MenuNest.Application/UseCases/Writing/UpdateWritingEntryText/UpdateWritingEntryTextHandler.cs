using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.UpdateWritingEntryText;

public sealed class UpdateWritingEntryTextHandler
    : ICommandHandler<UpdateWritingEntryTextCommand, WritingEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<UpdateWritingEntryTextCommand> _validator;

    public UpdateWritingEntryTextHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<UpdateWritingEntryTextCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<WritingEntryDto> Handle(UpdateWritingEntryTextCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var entry = await _db.WritingEntries
            .FirstOrDefaultAsync(w => w.Id == command.Id && w.UserId == user.Id && w.DeletedAt == null, ct)
            ?? throw new DomainException("Writing entry not found.");

        entry.UpdateText(command.Text);
        await _db.SaveChangesAsync(ct);

        return new WritingEntryDto(
            Id: entry.Id,
            Date: entry.Date,
            Text: entry.Text,
            ElapsedSeconds: entry.ElapsedSeconds,
            WordsPerMinute: entry.WordsPerMinute,
            CorrectedAt: entry.CorrectedAt,
            CreatedAt: entry.CreatedAt);
    }
}
