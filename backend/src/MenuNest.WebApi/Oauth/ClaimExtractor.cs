using System.IdentityModel.Tokens.Jwt;

namespace MenuNest.WebApi.Oauth;

public record UserIdentity(string Oid, string? Name, string? Email);

/// <summary>Pulls the identity claims MenuNest needs out of an Entra id_token.</summary>
public static class ClaimExtractor
{
    public static UserIdentity FromIdToken(string idToken)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(idToken);
        string? Get(string t) => jwt.Claims.FirstOrDefault(c => c.Type == t)?.Value;

        var oid = Get("oid") ?? Get("http://schemas.microsoft.com/identity/claims/objectidentifier") ?? Get("sub")
            ?? throw new InvalidOperationException("id_token has no oid/sub claim.");
        return new UserIdentity(oid, Get("name"), Get("email") ?? Get("preferred_username"));
    }
}
