using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace MenuNest.Application.UnitTests.Support;

/// <summary>
/// Disposable test fixture that wires up an InMemory DbContext + a
/// stub <see cref="IUserProvisioner"/> seeded with a single family
/// and user. Tests build on this so each one only seeds the rows it
/// actually cares about.
/// </summary>
public sealed class HandlerTestFixture : IDisposable
{
    public InMemoryAppDbContext Db { get; }
    public Mock<IUserProvisioner> UserProvisioner { get; }
    public Family Family { get; }
    public User User { get; }

    public HandlerTestFixture()
    {
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase($"menunest-tests-{Guid.NewGuid()}")
            .Options;
        Db = new InMemoryAppDbContext(options);

        User = User.CreateFromExternalLogin(
            externalId: "test-oid",
            email: "test@example.com",
            displayName: "Test User",
            authProvider: AuthProvider.Microsoft);
        Family = Family.CreateNew("Test Family", User.Id);
        User.JoinFamily(Family.Id);

        Db.Users.Add(User);
        Db.Families.Add(Family);
        Db.SaveChanges();

        UserProvisioner = new Mock<IUserProvisioner>();
        UserProvisioner
            .Setup(u => u.RequireFamilyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((User, Family.Id));
        UserProvisioner
            .Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(User);
    }

    public void Dispose() => Db.Dispose();
}
