using Mediator;

namespace MenuNest.Application.UseCases.Writing.RecordWritingCorrection;

/// <summary>
/// The one combined call carrying everything the critique loop produces for a
/// single entry (mcp-tool-contract's record_writing_correction). Marks the
/// entry corrected.
///
/// WordsPerMinute and target-errors-per-100-words are deliberately NOT inputs:
/// MenuNest already has elapsedSeconds and the text, and derives both numbers
/// itself from the hit/miss counts here plus the word count. Adding either as
/// an argument would move the computation into the AI's hands.
/// </summary>
public sealed record RecordWritingCorrectionCommand(
    Guid EntryId,
    string TargetRule,
    string MarkedText,
    int HitCount,
    int MissCount,
    string ThaiWhyLine,
    IReadOnlyList<SentenceCombiningItemDto> SentenceCombiningItems,
    IReadOnlyList<StuckWordDto> StuckWords) : ICommand<WritingEntryDto>;
