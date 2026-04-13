using MenuNest.Domain.Entities;

namespace MenuNest.Application.Abstractions;

/// <summary>
/// Resolves the current caller to a persistent <see cref="User"/> row,
/// creating one on the fly from the Entra ID JWT claims on first
/// sign-in. Handlers use this instead of manually querying the
/// <c>Users</c> DbSet so auto-provisioning stays consistent across
/// the whole Application layer.
/// </summary>
public interface IUserProvisioner
{
    Task<User> GetOrProvisionCurrentAsync(CancellationToken ct = default);
}
