using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using FluentValidation;

namespace MenuNest.Application.UseCases.Writing.SubmitWritingEntry;

public sealed class SubmitWritingEntryHandler : ICommandHandler<SubmitWritingEntryCommand, WritingEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<SubmitWritingEntryCommand> _validator;
    private readonly IClock _clock;

    public SubmitWritingEntryHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<SubmitWritingEntryCommand> validator,
        IClock clock)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
        _clock = clock;
    }

    public async ValueTask<WritingEntryDto> Handle(SubmitWritingEntryCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var entry = WritingEntry.Create(
            userId: user.Id,
            date: command.Date,
            text: command.Text,
            elapsedSeconds: command.ElapsedSeconds);

        _db.WritingEntries.Add(entry);
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
