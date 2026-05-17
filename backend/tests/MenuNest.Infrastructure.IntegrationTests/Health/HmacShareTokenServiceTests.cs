using FluentAssertions;
using MenuNest.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MenuNest.Infrastructure.IntegrationTests.Health;

/// <summary>
/// Round-trip + negative-path coverage for <see cref="HmacShareTokenService"/>.
/// No DB / network access — kept under Infrastructure tests so the JWT
/// stack (Microsoft.IdentityModel.Tokens) is referenced explicitly.
/// </summary>
public class HmacShareTokenServiceTests
{
    private const string ValidKey = "ZGV2LXNoYXJlLWtleS1vbmx5LTMyLWJ5dGVzLWxvbmchISEhISEh";
    private const string DifferentKey = "YW5vdGhlci1rZXktdGhhdC1pcy1hbHNvLTMyLWJ5dGVzLW9rIQ==";

    private static HmacShareTokenService Build(string key = ValidKey,
        string issuer = "menunest-share", string audience = "menunest-doctor")
        => new(Options.Create(new ShareOptions
        {
            TokenSigningKey = key,
            TokenIssuer = issuer,
            TokenAudience = audience,
        }));

    [Fact]
    public void Constructor_throws_when_signing_key_missing()
    {
        var act = () => new HmacShareTokenService(Options.Create(new ShareOptions
        {
            TokenSigningKey = null,
        }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Share:TokenSigningKey*");
    }

    [Fact]
    public void Constructor_throws_when_signing_key_too_short()
    {
        // Base64 of 8 bytes — well under the 16-byte HMAC minimum.
        var shortKey = Convert.ToBase64String(new byte[8]);
        var act = () => new HmacShareTokenService(Options.Create(new ShareOptions
        {
            TokenSigningKey = shortKey,
        }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*16 bytes*");
    }

    [Fact]
    public void Issue_then_Verify_round_trips_claims()
    {
        var sut = Build();
        var userId = Guid.NewGuid();
        var from = new DateOnly(2026, 04, 17);
        var to = new DateOnly(2026, 05, 17);
        var expires = DateTime.UtcNow.AddDays(30);

        var issuance = sut.Issue(userId, from, to, expires);

        issuance.RawToken.Should().NotBeNullOrWhiteSpace();
        issuance.Hash.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");

        var claims = sut.Verify(issuance.RawToken);
        claims.UserId.Should().Be(userId);
        claims.DateFrom.Should().Be(from);
        claims.DateTo.Should().Be(to);
        claims.ExpiresAtUtc.Should().BeCloseTo(expires, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Verify_rejects_tampered_token()
    {
        var sut = Build();
        var issuance = sut.Issue(Guid.NewGuid(), new DateOnly(2026, 4, 1),
            new DateOnly(2026, 5, 1), DateTime.UtcNow.AddDays(10));

        // Flip a character in the signature segment (the last "." section).
        // Mutating the payload sometimes produces a broken base64-url that
        // the IdentityModel reader rejects with ArgumentException before
        // the signature check runs — we want to test the signature path,
        // so target the signature specifically.
        var parts = issuance.RawToken.Split('.');
        parts[2] = parts[2][..^1] + (parts[2][^1] == 'A' ? 'B' : 'A');
        var tampered = string.Join('.', parts);

        var act = () => sut.Verify(tampered);
        // SecurityTokenInvalidSignatureException derives from SecurityTokenException;
        // assert on the base type so the IdentityModel internals don't pin us
        // to a particular subclass.
        act.Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void Verify_rejects_expired_token()
    {
        var sut = Build();
        var issuance = sut.Issue(Guid.NewGuid(), new DateOnly(2026, 4, 1),
            new DateOnly(2026, 5, 1), DateTime.UtcNow.AddSeconds(-30));

        var act = () => sut.Verify(issuance.RawToken);
        act.Should().Throw<SecurityTokenExpiredException>();
    }

    [Fact]
    public void Verify_rejects_token_signed_with_different_key()
    {
        var signer = Build(key: ValidKey);
        var verifier = Build(key: DifferentKey);

        var issuance = signer.Issue(Guid.NewGuid(), new DateOnly(2026, 4, 1),
            new DateOnly(2026, 5, 1), DateTime.UtcNow.AddDays(10));

        var act = () => verifier.Verify(issuance.RawToken);
        act.Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void Verify_rejects_token_with_wrong_issuer()
    {
        var signer = Build(issuer: "menunest-share");
        var verifier = Build(issuer: "different-issuer");

        var issuance = signer.Issue(Guid.NewGuid(), new DateOnly(2026, 4, 1),
            new DateOnly(2026, 5, 1), DateTime.UtcNow.AddDays(10));

        var act = () => verifier.Verify(issuance.RawToken);
        act.Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void Verify_rejects_token_with_wrong_audience()
    {
        var signer = Build(audience: "menunest-doctor");
        var verifier = Build(audience: "different-audience");

        var issuance = signer.Issue(Guid.NewGuid(), new DateOnly(2026, 4, 1),
            new DateOnly(2026, 5, 1), DateTime.UtcNow.AddDays(10));

        var act = () => verifier.Verify(issuance.RawToken);
        act.Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void Hash_is_sha256_hex_lowercase_and_deterministic()
    {
        var sut = Build();
        var hash1 = sut.Hash("identical-input");
        var hash2 = sut.Hash("identical-input");

        hash1.Should().HaveLength(64);
        hash1.Should().MatchRegex("^[0-9a-f]+$",
            "SHA-256 hex must be lowercase 0-9a-f only");
        hash2.Should().Be(hash1);

        sut.Hash("a-different-input").Should().NotBe(hash1);
    }
}
