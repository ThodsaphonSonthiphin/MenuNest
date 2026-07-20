using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace MenuNest.WebApi.Oauth;

public record ClientRegistration(string ClientId, string[] RedirectUris);
public record PkceFlow(string ClientRedirectUri, string ClientState, string ClientCodeChallenge, string OurVerifier, string Scope);
public record AuthCodeData(string ClientCodeChallenge, string Subject, string ClientId, string Scope, string? Name, string? Email, string EntraRefreshToken);

/// <summary>DCR client registrations. Durable in SQL (ADR-037).</summary>
public sealed class ClientStore(IApplicationDbContext db)
{
    public async Task<string> RegisterAsync(string name, string[] redirectUris, string? scope, CancellationToken ct = default)
    {
        var clientId = TokenUtil.Opaque(16);
        db.OAuthClients.Add(new OAuthClient
        {
            ClientId = clientId,
            ClientName = name,
            RedirectUrisJson = JsonSerializer.Serialize(redirectUris),
            Scope = scope,
            ExpiresAt = DateTime.UtcNow.AddDays(365),
        });
        await db.SaveChangesAsync(ct);
        return clientId;
    }

    public async Task<ClientRegistration?> GetAsync(string clientId, CancellationToken ct = default)
    {
        var row = await db.OAuthClients
            .FirstOrDefaultAsync(c => c.ClientId == clientId && c.ExpiresAt > DateTime.UtcNow, ct);
        return row is null
            ? null
            : new ClientRegistration(row.ClientId, JsonSerializer.Deserialize<string[]>(row.RedirectUrisJson) ?? Array.Empty<string>());
    }
}

/// <summary>Authorize→callback flow state. Short-lived, single-use.</summary>
public sealed class PkceStateStore(IMemoryCache cache)
{
    public string Save(PkceFlow flow)
    {
        var id = TokenUtil.Opaque();
        cache.Set($"flow:{id}", flow, TimeSpan.FromMinutes(10));
        return id;
    }

    public PkceFlow? Take(string flowId)
    {
        if (cache.TryGetValue($"flow:{flowId}", out PkceFlow? flow)) { cache.Remove($"flow:{flowId}"); return flow; }
        return null;
    }
}

/// <summary>Proxy auth codes (60s, in-memory) and durable refresh codes → Entra RT (SQL, ADR-037).</summary>
public sealed class TokenStore(IMemoryCache cache, IApplicationDbContext db)
{
    public string SaveAuthCode(AuthCodeData data)
    {
        var code = TokenUtil.Opaque();
        cache.Set($"code:{code}", data, TimeSpan.FromSeconds(60));
        return code;
    }

    public AuthCodeData? TakeAuthCode(string code)
    {
        if (cache.TryGetValue($"code:{code}", out AuthCodeData? d)) { cache.Remove($"code:{code}"); return d; }
        return null;
    }

    public async Task<string> SaveRefreshAsync(string entraRefreshToken, string subject, CancellationToken ct = default)
    {
        var refreshCode = TokenUtil.Opaque();
        db.OAuthRefreshTokens.Add(new OAuthRefreshToken
        {
            RefreshCode = refreshCode,
            EntraRefreshToken = entraRefreshToken,
            Subject = subject,
            ExpiresAt = DateTime.UtcNow.AddDays(365),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return refreshCode;
    }

    public async Task<string?> TakeRefreshAsync(string refreshCode, CancellationToken ct = default)
    {
        var row = await db.OAuthRefreshTokens.FirstOrDefaultAsync(r => r.RefreshCode == refreshCode, ct);
        if (row is null || row.ExpiresAt <= DateTime.UtcNow) return null;
        db.OAuthRefreshTokens.Remove(row); // single-use; a fresh code is minted by SaveRefreshAsync
        await db.SaveChangesAsync(ct);
        return row.EntraRefreshToken;
    }
}
