using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Writing;
using MenuNest.Application.UseCases.Writing.GetActiveTargetRule;
using MenuNest.Application.UseCases.Writing.ListPendingWritingEntries;
using MenuNest.Application.UseCases.Writing.RecordWritingCorrection;
using MenuNest.Application.UseCases.Writing.SetActiveTargetRule;
using MenuNest.McpServer.Tools;
using ModelContextProtocol.Server;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class WritingToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly WritingTools _sut;

    public WritingToolsTests() => _sut = new WritingTools(_mediator.Object);

    [Fact]
    public async Task list_pending_writing_entries_sends_the_query()
    {
        IReadOnlyList<PendingWritingEntryDto> expected = new List<PendingWritingEntryDto>
        {
            new(Guid.NewGuid(), new DateOnly(2026, 8, 16), "<p>pending night</p>", 41, 5.853658536585366),
        };
        _mediator
            .Setup(m => m.Send(It.IsAny<ListPendingWritingEntriesQuery>(), It.IsAny<CancellationToken>()))
            .Returns<ListPendingWritingEntriesQuery, CancellationToken>(
                (_, _) => new ValueTask<IReadOnlyList<PendingWritingEntryDto>>(expected));

        var result = await _sut.list_pending_writing_entries(CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.IsAny<ListPendingWritingEntriesQuery>(), It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task get_active_target_rule_sends_the_query()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<GetActiveTargetRuleQuery>(), It.IsAny<CancellationToken>()))
            .Returns<GetActiveTargetRuleQuery, CancellationToken>(
                (_, _) => new ValueTask<string?>("third-person singular -s"));

        var result = await _sut.get_active_target_rule(CancellationToken.None);

        result.Should().Be("third-person singular -s");
    }

    [Fact]
    public async Task get_active_target_rule_passes_through_a_null_unset_rule()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<GetActiveTargetRuleQuery>(), It.IsAny<CancellationToken>()))
            .Returns<GetActiveTargetRuleQuery, CancellationToken>((_, _) => new ValueTask<string?>((string?)null));

        var result = await _sut.get_active_target_rule(CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task set_active_target_rule_sends_the_command_with_the_rule()
    {
        _mediator
            .Setup(m => m.Send(
                It.Is<SetActiveTargetRuleCommand>(c => c.Rule == "articles (a/an/the)"),
                It.IsAny<CancellationToken>()))
            .Returns<SetActiveTargetRuleCommand, CancellationToken>(
                (_, _) => new ValueTask<string?>("articles (a/an/the)"));

        var result = await _sut.set_active_target_rule("articles (a/an/the)", CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<SetActiveTargetRuleCommand>(c => c.Rule == "articles (a/an/the)"),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().Be("articles (a/an/the)");
    }

    [Fact]
    public async Task record_writing_correction_sends_every_block_on_the_command()
    {
        var entryId = Guid.NewGuid();
        var expected = new WritingEntryDto(
            entryId, new DateOnly(2026, 8, 16), "<p>She go to school.</p>", 420, 8.1,
            new DateTime(2026, 8, 17, 9, 30, 0, DateTimeKind.Utc), new DateTime(2026, 8, 16, 13, 46, 59, DateTimeKind.Utc));
        _mediator
            .Setup(m => m.Send(It.IsAny<RecordWritingCorrectionCommand>(), It.IsAny<CancellationToken>()))
            .Returns<RecordWritingCorrectionCommand, CancellationToken>((_, _) => new ValueTask<WritingEntryDto>(expected));

        var result = await _sut.record_writing_correction(
            entryId: entryId,
            targetRule: "third-person singular -s",
            markedText: "<p>She <span class=\"miss\">go</span> <span class=\"fix\">→ goes</span> to school.</p>",
            hitCount: 0,
            missCount: 1,
            thaiWhyLine: "ประธานเป็น he / she / it → กริยาต้องเติม -s",
            sentenceCombiningItems: new List<SentenceCombiningItemDto>
            {
                new("Traffic is very bad. + We arrive late.", "Traffic was very bad, so we arrived late."),
            },
            stuckWords: new List<StuckWordDto> { new("ข้าวต้ม", "rice porridge / congee") },
            ct: CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<RecordWritingCorrectionCommand>(c =>
                c.EntryId == entryId &&
                c.TargetRule == "third-person singular -s" &&
                c.HitCount == 0 &&
                c.MissCount == 1 &&
                c.MarkedText.Contains("→ goes") &&
                c.ThaiWhyLine.Contains("เติม -s") &&
                c.SentenceCombiningItems.Count == 1 &&
                c.StuckWords.Count == 1 &&
                c.StuckWords[0].Thai == "ข้าวต้ม"),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public void Exposes_exactly_the_four_contracted_tools_and_no_create_or_edit_tool()
    {
        var toolNames = typeof(WritingTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => m.Name)
            .ToList();

        toolNames.Should().BeEquivalentTo(new[]
        {
            "list_pending_writing_entries",
            "get_active_target_rule",
            "set_active_target_rule",
            "record_writing_correction",
        });
        // Entry creation and text editing stay in-app, never MCP
        // (mcp-tool-contract.md:38).
        toolNames.Should().NotContain(n =>
            n.Contains("submit") || n.Contains("create") || n.Contains("update_writing") || n.Contains("delete"));
    }

    [Fact]
    public void Every_tool_and_every_parameter_carries_a_description()
    {
        var tools = typeof(WritingTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToList();

        tools.Should().HaveCount(4);
        foreach (var tool in tools)
        {
            tool.GetCustomAttribute<DescriptionAttribute>()
                .Should().NotBeNull($"{tool.Name} needs a [Description] so Claude Code knows when to call it");

            foreach (var p in tool.GetParameters().Where(p => p.ParameterType != typeof(CancellationToken)))
            {
                p.GetCustomAttribute<DescriptionAttribute>()
                    .Should().NotBeNull($"{tool.Name}.{p.Name} needs a [Description]");
            }
        }
    }

    [Fact]
    public void record_writing_correction_takes_no_derived_number_arguments()
    {
        // The contract is explicit: MenuNest computes words-per-minute and
        // target-errors-per-100-words itself. Accepting either as an argument
        // would move the computation into the AI's hands.
        var parameters = typeof(WritingTools)
            .GetMethod(nameof(WritingTools.record_writing_correction))!
            .GetParameters()
            .Select(p => p.Name!.ToLowerInvariant())
            .ToList();

        parameters.Should().NotContain(p => p.Contains("wordsperminute") || p.Contains("wpm"));
        parameters.Should().NotContain(p => p.Contains("per100") || p.Contains("errorrate"));
    }
}
