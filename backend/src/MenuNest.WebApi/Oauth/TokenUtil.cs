using System.Security.Cryptography;

namespace MenuNest.WebApi.Oauth;

public static class TokenUtil
{
    public static string Opaque(int byteLength = 32)
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLength))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
