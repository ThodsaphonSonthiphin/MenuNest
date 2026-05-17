using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MenuNest.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MenuNest.Infrastructure.Services;

/// <summary>
/// Default <see cref="IShareTokenService"/>. Produces JWTs signed with
/// HMAC-SHA256, where the signing key comes from
/// <see cref="ShareOptions.TokenSigningKey"/> (base64-encoded random
/// bytes — recommended &gt;= 32 bytes).
///
/// Claims layout: <c>sub</c>=userId, <c>df</c>=date-from (ISO date),
/// <c>dt</c>=date-to (ISO date), <c>exp</c>=unix seconds, <c>jti</c>=random
/// guid. Verification rejects tampered tokens, expired tokens, and tokens
/// minted for a different issuer/audience or with a different key.
/// </summary>
public sealed class HmacShareTokenService : IShareTokenService
{
    private readonly ShareOptions _options;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly JwtSecurityTokenHandler _handler;
    private readonly TokenValidationParameters _validationParameters;

    public HmacShareTokenService(IOptions<ShareOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.TokenSigningKey))
        {
            throw new InvalidOperationException(
                "Share:TokenSigningKey is not configured. Set it in appsettings.Development.json " +
                "or via App Service config in production (base64-encoded >= 32 random bytes).");
        }

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(_options.TokenSigningKey);
        }
        catch (FormatException)
        {
            // Fall back to raw UTF-8 bytes if the operator forgot the base64
            // encoding step. Tests sometimes pass plain ASCII for readability.
            keyBytes = Encoding.UTF8.GetBytes(_options.TokenSigningKey);
        }

        if (keyBytes.Length < 16)
        {
            throw new InvalidOperationException(
                "Share:TokenSigningKey decoded to fewer than 16 bytes — HMAC-SHA256 requires at least " +
                "128 bits of key material (recommended: 32 random bytes).");
        }

        _signingKey = new SymmetricSecurityKey(keyBytes);
        _handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.TokenIssuer,
            ValidateAudience = true,
            ValidAudience = _options.TokenAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            // No clock skew — share tokens are short-lived and we want
            // immediate rejection once they expire.
            ClockSkew = TimeSpan.Zero,
        };
    }

    public ShareTokenIssuance Issue(
        Guid userId,
        DateOnly dateFrom,
        DateOnly dateTo,
        DateTime expiresAtUtc)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (dateTo < dateFrom)
            throw new ArgumentException("DateTo must be on or after DateFrom.", nameof(dateTo));
        if (expiresAtUtc.Kind != DateTimeKind.Utc && expiresAtUtc.Kind != DateTimeKind.Unspecified)
            expiresAtUtc = expiresAtUtc.ToUniversalTime();

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        // Subject and date claims live in the payload. ExpiresAtUtc is
        // emitted as the standard "exp" claim via TokenDescriptor.Expires.
        var token = new JwtSecurityToken(
            issuer: _options.TokenIssuer,
            audience: _options.TokenAudience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
                new Claim("df", dateFrom.ToString("yyyy-MM-dd")),
                new Claim("dt", dateTo.ToString("yyyy-MM-dd")),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: expiresAtUtc,
            signingCredentials: credentials);

        var rawToken = _handler.WriteToken(token);
        var hash = Hash(rawToken);
        return new ShareTokenIssuance(rawToken, hash);
    }

    public ShareTokenClaims Verify(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new SecurityTokenException("Share token is missing.");

        var principal = _handler.ValidateToken(
            rawToken,
            _validationParameters,
            out var validated);

        var jwt = (JwtSecurityToken)validated;

        var subClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? throw new SecurityTokenException("Share token missing 'sub' claim.");
        if (!Guid.TryParse(subClaim, out var userId))
            throw new SecurityTokenException("Share token 'sub' is not a valid user id.");

        var dfClaim = principal.FindFirst("df")?.Value
            ?? throw new SecurityTokenException("Share token missing 'df' claim.");
        var dtClaim = principal.FindFirst("dt")?.Value
            ?? throw new SecurityTokenException("Share token missing 'dt' claim.");

        if (!DateOnly.TryParseExact(dfClaim, "yyyy-MM-dd", out var dateFrom))
            throw new SecurityTokenException("Share token 'df' is not a valid ISO date.");
        if (!DateOnly.TryParseExact(dtClaim, "yyyy-MM-dd", out var dateTo))
            throw new SecurityTokenException("Share token 'dt' is not a valid ISO date.");

        // jwt.ValidTo is already a UTC DateTime per the JWT spec.
        var expiresAt = DateTime.SpecifyKind(jwt.ValidTo, DateTimeKind.Utc);

        return new ShareTokenClaims(userId, dateFrom, dateTo, expiresAt);
    }

    public string Hash(string rawToken)
    {
        if (rawToken is null)
            throw new ArgumentNullException(nameof(rawToken));

        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
