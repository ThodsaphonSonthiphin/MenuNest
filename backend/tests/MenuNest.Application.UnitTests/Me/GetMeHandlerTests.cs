using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Me.GetMe;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Me;

public sealed class GetMeHandlerTests
{
    private static SqliteAppDbContext NewContext(SqliteConnection conn)
    {
        var ctx = new SqliteAppDbContext(
            new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task Returns_HomePath_when_a_UserSettings_row_exists()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);

        var user = User.CreateFromExternalLogin("ext-1", "a@b.com", "A", AuthProvider.Microsoft);
        ctx.Users.Add(user);
        var settings = UserSettings.Create(user.Id);
        settings.SetHomePath("/trips");
        ctx.UserSettings.Add(settings);
        await ctx.SaveChangesAsync();

        var provisioner = new Mock<IUserProvisioner>();
        provisioner.Setup(p => p.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var handler = new GetMeHandler(provisioner.Object, ctx);
        var me = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        me.HomePath.Should().Be("/trips");
    }

    [Fact]
    public async Task Returns_null_HomePath_when_no_UserSettings_row()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);

        var user = User.CreateFromExternalLogin("ext-2", "b@b.com", "B", AuthProvider.Microsoft);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var provisioner = new Mock<IUserProvisioner>();
        provisioner.Setup(p => p.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var handler = new GetMeHandler(provisioner.Object, ctx);
        var me = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        me.HomePath.Should().BeNull();
    }
}