using FluentAssertions;
using Mediator;
using MenuNest.Application.UseCases.Writing;
using MenuNest.Application.UseCases.Writing.GetWritingEntry;
using MenuNest.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace MenuNest.WebApi.UnitTests.Controllers;

/// <summary>
/// Wire-boundary test for GET /api/writing-entries/{id}. The action must bind
/// the route id into GetWritingEntryQuery and return the detail DTO unchanged;
/// a test that sends the query directly proves nothing about the controller.
/// </summary>
public sealed class WritingEntriesControllerGetByIdTests
{
    [Fact]
    public async Task GetById_sends_the_route_id_as_the_query_and_returns_the_detail_dto()
    {
        var mediator = new Mock<IMediator>();
        GetWritingEntryQuery? captured = null;
        var id = Guid.NewGuid();

        var expected = new WritingEntryDetailDto(
            Id: id,
            Date: new DateOnly(2026, 8, 16),
            Text: "<p>one two three</p>",
            ElapsedSeconds: 420,
            WordsPerMinute: 0.43,
            CorrectedAt: new DateTime(2026, 8, 17, 14, 57, 23, DateTimeKind.Utc),
            CreatedAt: new DateTime(2026, 8, 16, 15, 0, 0, DateTimeKind.Utc),
            Correction: new WritingCorrectionDto(
                TargetRule: "articles (a/an/the)",
                MarkedText: "<p><span class=\"hit\">one</span> two three</p>",
                HitCount: 1,
                MissCount: 0,
                ThaiWhyLine: "คำนามนับได้เอกพจน์ต้องมีตัวนำหน้าเสมอ",
                SentenceCombiningItems: [],
                StuckWords: [],
                ErrorsPer100Words: 0));

        mediator
            .Setup(m => m.Send(It.IsAny<GetWritingEntryQuery>(), It.IsAny<CancellationToken>()))
            .Callback<IQuery<WritingEntryDetailDto>, CancellationToken>((q, _) => captured = (GetWritingEntryQuery)q)
            .Returns(new ValueTask<WritingEntryDetailDto>(expected));

        var controller = new WritingEntriesController(mediator.Object);

        var result = await controller.GetById(id, CancellationToken.None);

        captured.Should().NotBeNull("the controller must send exactly one GetWritingEntryQuery");
        captured!.Id.Should().Be(id, "the route id must reach the query unchanged");

        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.Value.Should().BeSameAs(expected, "the controller must not reshape the DTO");
    }
}
