using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.Authentication;

/// <summary>
/// Default <see cref="IUserProvisioner"/> implementation. Looks up
/// the caller's <c>User</c> row by <c>ExternalId</c> (the Entra
/// <c>oid</c> claim). If none exists yet, creates one from the
/// current token's email + name claims and persists it.
/// </summary>
internal sealed class UserProvisioner : IUserProvisioner
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UserProvisioner(AppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<User> GetOrProvisionCurrentAsync(CancellationToken ct = default)
    {
        var externalId = _currentUser.RequireExternalId();

        var existing = await _db.Users
            .Include(u => u.Family)
            .FirstOrDefaultAsync(u => u.ExternalId == externalId, ct);

        if (existing is not null)
        {
            return existing;
        }

        var email = _currentUser.Email ?? $"{externalId}@unknown";
        var displayName = _currentUser.DisplayName ?? email;

        var user = User.CreateFromEntraClaim(externalId, email, displayName);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }
}
