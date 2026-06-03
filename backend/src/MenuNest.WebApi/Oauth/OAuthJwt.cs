using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MenuNest.WebApi.Oauth;

/// <summary>
/// Mints and validates the proxy's own access token. aud == iss == MCP:ServerUrl
/// (RFC 8707 audience binding). HMAC-SHA256, key derived from Jwt:SigningKey.
/// Adapted from the MIT-licensed Profility/mcp-server-dotnet-entra-id reference.
/// </summary>
public sealed class OAuthJwt
{
    private readonly string _serverUrl;
    private readonly SymmetricSecurityKey _key;

    public OAuthJwt(Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _serverUrl = config["MCP:ServerUrl"]
            ?? throw new InvalidOperationException("MCP:ServerUrl is not configured.");
        var signingKey = config["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");
        _key = new SymmetricSecurityKey(SHA256.HashData(Encoding.UTF8.GetBytes(signingKey)));
    }

    public string Mint(string subject, string clientId, string scope, IEnumerable<Claim> extra, int lifetimeSeconds = 3600)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new("sub", subject),
            new("oid", subject),
            new("client_id", clientId),
            new("scope", scope),
            new("jti", Guid.NewGuid().ToString("N")),
        };
        claims.AddRange(extra);

        var token = new JwtSecurityToken(
            issuer: _serverUrl,
            audience: _serverUrl,
            claims: claims,
            notBefore: now,
            expires: now.AddSeconds(lifetimeSeconds),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = _serverUrl,
        ValidAudience = _serverUrl,
        IssuerSigningKey = _key,
        ClockSkew = TimeSpan.FromMinutes(5),
    };
}
