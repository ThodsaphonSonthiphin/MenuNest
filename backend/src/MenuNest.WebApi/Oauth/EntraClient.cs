using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace MenuNest.WebApi.Oauth;

public record EntraTokens(string AccessToken, string? RefreshToken, string? IdToken, int ExpiresIn);

/// <summary>Drives the clean Entra authorization-code+PKCE flow server-side — NO RFC 8707 resource param.</summary>
public sealed class EntraClient(HttpClient http, IConfiguration config)
{
    private string Instance => config["AzureAd:Instance"]!.TrimEnd('/');
    private string Tenant => config["AzureAd:TenantId"]!;
    private string ClientId => config["AzureAd:ClientId"]!;
    private string ClientSecret => config["AzureAd:ClientSecret"]!;
    private string ServerBase => config["MCP:ServerUrl"]!.Replace("/mcp", "");
    private string Callback => $"{ServerBase}/oauth/callback";
    private string Scope => $"api://{ClientId}/access_as_user offline_access openid profile email";

    public string BuildAuthorizeUrl(string state, string codeChallenge)
        => $"{Instance}/{Tenant}/oauth2/v2.0/authorize"
         + $"?client_id={Uri.EscapeDataString(ClientId)}"
         + "&response_type=code"
         + $"&redirect_uri={Uri.EscapeDataString(Callback)}"
         + $"&scope={Uri.EscapeDataString(Scope)}"
         + $"&state={Uri.EscapeDataString(state)}"
         + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
         + "&code_challenge_method=S256"
         + "&prompt=select_account";

    public Task<EntraTokens> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
        => PostAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = Callback,
            ["code_verifier"] = codeVerifier,
            ["scope"] = Scope,
        }, ct);

    public Task<EntraTokens> RefreshAsync(string refreshToken, CancellationToken ct)
        => PostAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope,
        }, ct);

    private async Task<EntraTokens> PostAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var url = $"{Instance}/{Tenant}/oauth2/v2.0/token";
        using var resp = await http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Entra token endpoint failed ({(int)resp.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new EntraTokens(
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            root.TryGetProperty("id_token", out var it) ? it.GetString() : null,
            root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600);
    }
}
