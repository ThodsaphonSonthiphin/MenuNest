using System.Security.Cryptography;
using System.Text;

namespace MenuNest.WebApi.Oauth;

/// <summary>PKCE (RFC 7636) S256 helpers.</summary>
public static class PkceUtil
{
    public static string GenerateVerifier()
        => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Challenge(string verifier)
        => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static bool Verify(string verifier, string challenge)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Challenge(verifier)),
            Encoding.ASCII.GetBytes(challenge));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
