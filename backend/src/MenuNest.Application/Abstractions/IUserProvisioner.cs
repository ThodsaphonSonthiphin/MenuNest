using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

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

    /// <summary>
    /// Family-scoped helper: returns the current <see cref="User"/>
    /// and their <c>FamilyId</c>, or throws <see cref="DomainException"/>
    /// if the caller has not joined a family yet. Handlers for
    /// family-scoped resources (ingredients, recipes, stock, …) use
    /// this to fail fast with a clean 400 instead of silently
    /// returning empty results.
    /// </summary>
    Task<(User User, Guid FamilyId)> RequireFamilyAsync(CancellationToken ct = default);
}
