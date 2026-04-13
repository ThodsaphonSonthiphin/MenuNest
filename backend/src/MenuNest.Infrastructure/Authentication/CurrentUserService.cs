using System.Security.Claims;
using MenuNest.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace MenuNest.Infrastructure.Authentication;

/// <summary>
/// Reads identity information from the Entra ID JWT on the current
/// HTTP request. Registered per-scope so it matches the request
/// lifetime.
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    // Standard Microsoft Identity claim type URIs. `oid` is published
    // under this URI on tokens issued by v1.0 and v2.0 endpoints.
    private const string ObjectIdClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string ShortObjectIdClaim = "oid";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public string? ExternalId =>
        Principal?.FindFirstValue(ObjectIdClaim)
        ?? Principal?.FindFirstValue(ShortObjectIdClaim);

    public string? Email =>
        Principal?.FindFirstValue(ClaimTypes.Email)
        ?? Principal?.FindFirstValue("email")
        ?? Principal?.FindFirstValue("preferred_username");

    public string? DisplayName =>
        Principal?.FindFirstValue("name")
        ?? Principal?.FindFirstValue(ClaimTypes.Name);

    public string RequireExternalId()
    {
        if (!IsAuthenticated || string.IsNullOrEmpty(ExternalId))
        {
            throw new UnauthorizedAccessException("No authenticated user on the current request.");
        }
        return ExternalId;
    }
}
