using MenuNest.Application.UseCases.Writing;
using MenuNest.Application.UseCases.Writing.GetActiveTargetRule;
using MenuNest.Application.UseCases.Writing.ListPendingWritingEntries;
using MenuNest.Application.UseCases.Writing.RecordWritingCorrection;
using MenuNest.Application.UseCases.Writing.SetActiveTargetRule;

namespace MenuNest.McpServer.Tools;

/// <summary>
/// The writing-practice correction loop over MCP (issue #97 Phase 2,
/// ai-correction-invocation Path B). Four tools, and deliberately no fifth:
/// creating or editing an entry stays in the MenuNest page's own submit button,
/// never MCP (mcp-tool-contract).
/// </summary>
[McpServerToolType]
public sealed class WritingTools(IMediator mediator)
{
    [McpServerTool, Description(
        "List the writer's freewrite nights that have NO correction yet, newest first. Call this FIRST to find what needs correcting — the writer does not have to name a date. Returns id, date, text (RTE HTML, with Thai stuck-words in [square brackets]), elapsedSeconds and wordsPerMinute. An empty list means every night is already corrected.")]
    public async Task<IReadOnlyList<PendingWritingEntryDto>> list_pending_writing_entries(CancellationToken ct)
        => await mediator.Send(new ListPendingWritingEntriesQuery(), ct);

    [McpServerTool, Description(
        "Get the ONE grammar rule to grade tonight's writing against, e.g. 'third-person singular -s'. Returns null when the writer has never set one — in that case ASK them which rule they want this month and call set_active_target_rule before correcting anything. Never guess a rule.")]
    public async Task<string?> get_active_target_rule(CancellationToken ct)
        => await mediator.Send(new GetActiveTargetRuleQuery(), ct);

    [McpServerTool, Description(
        "Change the active target grammar rule. The writer normally flips this on MenuNest's settings screen; this tool is the same underlying value, for when they ask in chat instead ('change my rule to articles'). Pass an empty string to clear it back to unset. Returns the stored rule.")]
    public async Task<string?> set_active_target_rule(
        [Description("The new target grammar rule, max 200 chars, e.g. 'articles (a/an/the)'. Empty clears it.")] string rule,
        CancellationToken ct)
        => await mediator.Send(new SetActiveTargetRuleCommand(rule), ct);

    [McpServerTool, Description(
        "Record the complete 5-block correction for ONE night and mark it corrected. Grade against the ONE active target rule only — mark its instances in place and leave every other error (articles, tense, spelling) untouched and unmentioned. Never rewrite the writer's text, never score or praise. Re-calling this on an already-corrected night OVERWRITES the previous correction, which is how a bad pass is repaired. Do NOT pass words-per-minute or errors-per-100-words: MenuNest computes both itself.")]
    public async Task<WritingEntryDto> record_writing_correction(
        [Description("The entry id from list_pending_writing_entries")] Guid entryId,
        [Description("The rule this correction graded against — normally the value get_active_target_rule returned")] string targetRule,
        [Description("The writer's ORIGINAL text with only this rule's instances marked in place. A miss: <span class=\"miss\">go</span> <span class=\"fix\">→ goes</span>. A hit: <span class=\"hit\">is</span>. Keep the writer's [Thai brackets] as <span class=\"th\">[ข้าวต้ม]</span>. Copy every other word through verbatim. Max 50,000 chars.")] string markedText,
        [Description("How many instances of the target rule the writer got RIGHT, counted mechanically. 0 is valid (e.g. a Thai-only night has no instances at all).")] int hitCount,
        [Description("How many instances of the target rule the writer got WRONG, counted mechanically. 0 is valid.")] int missCount,
        [Description("ONE line in Thai explaining why the rule holds — the mechanism, not a translation. Max 2,000 chars.")] string thaiWhyLine,
        [Description("0-4 sentence-combining items built from the writer's OWN sentences that night. Each carries source ('Traffic is very bad. + We arrive late.') and combined ('Traffic was very bad, so we arrived late.'). Send an empty list when the night has no English sentences to combine.")] IReadOnlyList<SentenceCombiningItemDto> sentenceCombiningItems,
        [Description("The Thai words the writer bracketed because he could not produce them in English, each with its English translation, e.g. thai 'ข้าวต้ม' / english 'rice porridge / congee'. Empty list when there were none.")] IReadOnlyList<StuckWordDto> stuckWords,
        CancellationToken ct)
        => await mediator.Send(
            new RecordWritingCorrectionCommand(
                entryId, targetRule, markedText, hitCount, missCount,
                thaiWhyLine, sentenceCombiningItems, stuckWords),
            ct);
}
