using System.Text.Encodings.Web;
using System.Text.Json;
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.RecordWritingCorrection;

public sealed class RecordWritingCorrectionHandler
    : ICommandHandler<RecordWritingCorrectionCommand, WritingEntryDto>
{
    /// <summary>
    /// Thai must land in the column as real characters. The default encoder
    /// escapes every non-ASCII codepoint to \uXXXX — valid JSON, but it
    /// bloats the column and makes the stored data unreadable.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<RecordWritingCorrectionCommand> _validator;
    private readonly IClock _clock;

    public RecordWritingCorrectionHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<RecordWritingCorrectionCommand> validator,
        IClock clock)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
        _clock = clock;
    }

    public async ValueTask<WritingEntryDto> Handle(
        RecordWritingCorrectionCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        // Same guard and same message as every other writing handler — "not
        // found" for a missing, deleted, or foreign entry alike, so the message
        // never confirms that someone else's id exists.
        var entry = await _db.WritingEntries
            .FirstOrDefaultAsync(w => w.Id == command.EntryId && w.UserId == user.Id && w.DeletedAt == null, ct)
            ?? throw new DomainException("Writing entry not found.");

        entry.RecordCorrection(
            correctedAtUtc: _clock.UtcNow,
            targetRule: command.TargetRule,
            markedText: command.MarkedText,
            hitCount: command.HitCount,
            missCount: command.MissCount,
            thaiWhyLine: command.ThaiWhyLine,
            sentenceCombiningItemsJson: JsonSerializer.Serialize(command.SentenceCombiningItems, JsonOptions),
            stuckWordsJson: JsonSerializer.Serialize(command.StuckWords, JsonOptions));

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
