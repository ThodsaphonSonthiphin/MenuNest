using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Writing.SubmitWritingEntry;

namespace MenuNest.Application.UnitTests.Writing;

public class SubmitWritingEntryHandlerTests
{
    private static readonly DateTime FixedNow =
        new(2026, 08, 16, 22, 30, 00, DateTimeKind.Utc);

    private static SubmitWritingEntryHandler Build(HandlerTestFixture fx, FixedClock clock)
        => new(fx.Db, fx.UserProvisioner.Object, new SubmitWritingEntryValidator(), clock);

    [Fact]
    public async Task Creates_entry_scoped_to_current_user_with_computed_words_per_minute()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var result = await Build(fx, clock).Handle(
            new SubmitWritingEntryCommand(
                Date: new DateOnly(2026, 8, 16),
                Text: "<p>my daughter play with her toy all morning today</p>",
                ElapsedSeconds: 420),
            CancellationToken.None);

        result.Date.Should().Be(new DateOnly(2026, 8, 16));
        result.ElapsedSeconds.Should().Be(420);
        result.WordsPerMinute.Should().BeApproximately(9.0 / 7.0, 0.001);

        var stored = fx.Db.WritingEntries.Single();
        stored.UserId.Should().Be(fx.User.Id);
        stored.CorrectedAt.Should().BeNull();
    }

    [Fact]
    public async Task Validator_rejects_empty_text()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var act = async () => await Build(fx, clock).Handle(
            new SubmitWritingEntryCommand(new DateOnly(2026, 8, 16), "", 420),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Validator_rejects_non_positive_elapsed_seconds()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);

        var act = async () => await Build(fx, clock).Handle(
            new SubmitWritingEntryCommand(new DateOnly(2026, 8, 16), "<p>hi there today</p>", 0),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Two_entries_same_user_same_day_are_both_allowed()
    {
        // No uniqueness decision was made for (UserId, Date) — a return-night
        // rewrite or a second sitting should not be silently blocked.
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var handler = Build(fx, clock);

        await handler.Handle(
            new SubmitWritingEntryCommand(new DateOnly(2026, 8, 16), "<p>first entry today</p>", 420),
            CancellationToken.None);
        await handler.Handle(
            new SubmitWritingEntryCommand(new DateOnly(2026, 8, 16), "<p>second entry today</p>", 420),
            CancellationToken.None);

        fx.Db.WritingEntries.Count().Should().Be(2);
    }
}
