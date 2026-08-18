using System.Text.Json;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.GetWritingEntry;

public sealed class GetWritingEntryHandler
    : IQueryHandler<GetWritingEntryQuery, WritingEntryDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public GetWritingEntryHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<WritingEntryDetailDto> Handle(
        GetWritingEntryQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        // Same guard and same message as every other writing handler — "not
        // found" for a missing, deleted, or foreign entry alike.
        var entry = await _db.WritingEntries
            .FirstOrDefaultAsync(w => w.Id == query.Id && w.UserId == user.Id && w.DeletedAt == null, ct)
            ?? throw new DomainException("Writing entry not found.");

        return new WritingEntryDetailDto(
            Id: entry.Id,
            Date: entry.Date,
            Text: entry.Text,
            ElapsedSeconds: entry.ElapsedSeconds,
            WordsPerMinute: entry.WordsPerMinute,
            CorrectedAt: entry.CorrectedAt,
            CreatedAt: entry.CreatedAt,
            Correction: BuildCorrection(entry));
    }

    private static WritingCorrectionDto? BuildCorrection(WritingEntry entry)
    {
        if (entry.CorrectedAt is null) return null;

        var missCount = entry.MissCount ?? 0;
        var wordCount = WritingEntry.CountWords(entry.Text);
        var errorsPer100Words = wordCount == 0
            ? 0d
            : Math.Round(missCount * 100d / wordCount, 1, MidpointRounding.AwayFromZero);

        return new WritingCorrectionDto(
            TargetRule: entry.TargetRule ?? string.Empty,
            MarkedText: entry.MarkedText ?? string.Empty,
            HitCount: entry.HitCount ?? 0,
            MissCount: missCount,
            ThaiWhyLine: entry.ThaiWhyLine ?? string.Empty,
            SentenceCombiningItems: DeserialiseList<SentenceCombiningItemDto>(entry.SentenceCombiningItemsJson),
            StuckWords: DeserialiseList<StuckWordDto>(entry.StuckWordsJson),
            ErrorsPer100Words: errorsPer100Words);
    }

    /// <summary>
    /// Default JsonSerializerOptions on purpose: RecordWritingCorrectionHandler
    /// writes the records with default (PascalCase) naming, so the stored text
    /// is {"Thai":…}. A camelCase policy here would deserialise every field to
    /// an empty string silently.
    /// </summary>
    private static IReadOnlyList<T> DeserialiseList<T>(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? Array.Empty<T>()
            : JsonSerializer.Deserialize<List<T>>(json) ?? [];
}
