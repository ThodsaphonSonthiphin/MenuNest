using Microsoft.Extensions.Caching.Memory;

namespace MenuNest.WebApi.Oauth;

public record ClientRegistration(string ClientId, string[] RedirectUris);
public record PkceFlow(string ClientRedirectUri, string ClientState, string ClientCodeChallenge, string OurVerifier, string Scope);
public record AuthCodeData(string ClientCodeChallenge, string Subject, string ClientId, string Scope, string? Name, string? Email, string EntraRefreshToken);

/// <summary>DCR client registrations. In-memory; single-instance assumption (see ADR-003).</summary>
public sealed class ClientStore(IMemoryCache cache)
{
    public string Register(string name, string[] redirectUris, string? scope)
    {
        var clientId = TokenUtil.Opaque(16);
        cache.Set($"client:{clientId}", new ClientRegistration(clientId, redirectUris),
            TimeSpan.FromDays(30));
        return clientId;
    }

    public bool TryGet(string clientId, out ClientRegistration reg)
        => cache.TryGetValue($"client:{clientId}", out reg!);
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

/// <summary>Proxy auth codes (60s, single-use) and opaque refresh codes → Entra refresh token.</summary>
public sealed class TokenStore(IMemoryCache cache)
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

    public string SaveRefresh(string entraRefreshToken)
    {
        var refreshCode = TokenUtil.Opaque();
        cache.Set($"refresh:{refreshCode}", entraRefreshToken, TimeSpan.FromDays(30));
        return refreshCode;
    }

    public string? TakeRefresh(string refreshCode)
    {
        if (cache.TryGetValue($"refresh:{refreshCode}", out string? rt)) { cache.Remove($"refresh:{refreshCode}"); return rt; }
        return null;
    }
}
