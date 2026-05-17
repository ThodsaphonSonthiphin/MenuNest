using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.Share.CreateShareLink;
using Moq;

namespace MenuNest.Application.UnitTests.Health.Share;

public class CreateShareLinkHandlerTests
{
    private static readonly DateTime FixedNow =
        new(2026, 05, 17, 12, 00, 00, DateTimeKind.Utc);

    /// <summary>
    /// Simple stub <see cref="IShareTokenService"/> for tests — deterministic
    /// raw token + sha256 hash so we don't depend on JWT internals here
    /// (those are covered in <c>HmacShareTokenServiceTests</c>).
    /// </summary>
    private sealed class StubShareTokenService : IShareTokenService
    {
        public string LastRaw { get; private set; } = "";

        public ShareTokenIssuance Issue(Guid userId, DateOnly dateFrom, DateOnly dateTo, DateTime expiresAtUtc)
        {
            LastRaw = $"tok-{userId:N}-{dateFrom:yyyyMMdd}-{dateTo:yyyyMMdd}";
            return new ShareTokenIssuance(LastRaw, Hash(LastRaw));
        }

        public ShareTokenClaims Verify(string rawToken) => throw new NotImplementedException();

        public string Hash(string rawToken)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }

    private sealed class StubUrlBuilder : IShareUrlBuilder
    {
        public string BuildShareUrl(string rawToken) => $"https://example.com/share/{rawToken}";
    }

    private static CreateShareLinkHandler Build(
        HandlerTestFixture fx,
        FixedClock clock,
        IShareTokenService tokens,
        IShareUrlBuilder urlBuilder)
        => new(fx.Db, fx.UserProvisioner.Object, tokens, urlBuilder, clock, new CreateShareLinkValidator());

    [Fact]
    public async Task Issues_token_persists_hash_and_returns_share_url()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var tokens = new StubShareTokenService();
        var urlBuilder = new StubUrlBuilder();

        var from = new DateOnly(2026, 04, 17);
        var to = new DateOnly(2026, 05, 17);

        var sut = Build(fx, clock, tokens, urlBuilder);

        var result = await sut.Handle(
            new CreateShareLinkCommand(from, to, ValidForDays: 30),
            CancellationToken.None);

        result.Token.Should().Be(tokens.LastRaw);
        result.ShareUrl.Should().Be($"https://example.com/share/{tokens.LastRaw}");
        result.DateFrom.Should().Be(from);
        result.DateTo.Should().Be(to);
        result.ExpiresAt.Should().Be(FixedNow.AddDays(30));

        var persisted = fx.Db.ShareLinks.Single(l => l.Id == result.ShareId);
        persisted.UserId.Should().Be(fx.User.Id);
        persisted.DateFrom.Should().Be(from);
        persisted.DateTo.Should().Be(to);
        persisted.ExpiresAt.Should().Be(FixedNow.AddDays(30));
        persisted.TokenHash.Should().Be(tokens.Hash(tokens.LastRaw),
            "we persist the hash so a DB leak cannot recover live tokens");
        persisted.TokenHash.Should().NotContain(tokens.LastRaw,
            "the raw token must never appear in the DB column");
    }

    [Fact]
    public async Task Throws_ValidationException_when_date_to_before_date_from()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var tokens = new StubShareTokenService();
        var sut = Build(fx, clock, tokens, new StubUrlBuilder());

        var act = async () => await sut.Handle(
            new CreateShareLinkCommand(
                DateFrom: new DateOnly(2026, 05, 17),
                DateTo: new DateOnly(2026, 05, 16)),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(91)]
    [InlineData(365)]
    public async Task Throws_ValidationException_when_valid_for_days_out_of_range(int days)
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var sut = Build(fx, clock, new StubShareTokenService(), new StubUrlBuilder());

        var act = async () => await sut.Handle(
            new CreateShareLinkCommand(
                DateFrom: new DateOnly(2026, 05, 01),
                DateTo: new DateOnly(2026, 05, 17),
                ValidForDays: days),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Allows_single_day_range_when_from_equals_to()
    {
        using var fx = new HandlerTestFixture();
        var clock = new FixedClock(FixedNow);
        var same = new DateOnly(2026, 05, 17);

        var sut = Build(fx, clock, new StubShareTokenService(), new StubUrlBuilder());

        var result = await sut.Handle(
            new CreateShareLinkCommand(same, same, ValidForDays: 7),
            CancellationToken.None);

        result.DateFrom.Should().Be(same);
        result.DateTo.Should().Be(same);
        result.ExpiresAt.Should().Be(FixedNow.AddDays(7));
    }
}
