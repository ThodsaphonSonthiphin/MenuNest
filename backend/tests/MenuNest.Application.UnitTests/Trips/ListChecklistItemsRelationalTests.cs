using System.Data.Common;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Trips.ListChecklistItems;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips;

public sealed class ListChecklistItemsRelationalTests : IDisposable
{
    private readonly DbConnection _conn;
    private readonly SqliteAppDbContext _db;
    private readonly User _user;

    public ListChecklistItemsRelationalTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        _user = User.CreateFromExternalLogin("oid", "t@example.com", "Test", AuthProvider.Microsoft);
        _db.Users.Add(_user);
        _db.SaveChanges();
    }

    [Fact]
    public async Task Returns_only_current_users_items_ordered_by_name()
    {
        var other = User.CreateFromExternalLogin("oid2", "o@example.com", "Other", AuthProvider.Microsoft);
        _db.Users.Add(other);
        _db.ChecklistItems.Add(ChecklistItem.Create(_user.Id, "หมวก"));
        _db.ChecklistItems.Add(ChecklistItem.Create(_user.Id, "ครีมกันแดด"));
        _db.ChecklistItems.Add(ChecklistItem.Create(other.Id, "ของคนอื่น"));
        await _db.SaveChangesAsync();

        var users = new Mock<IUserProvisioner>();
        users.Setup(u => u.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        var handler = new ListChecklistItemsHandler(_db, users.Object);

        var result = await handler.Handle(new ListChecklistItemsQuery(), CancellationToken.None);

        result.Select(i => i.Name).Should().Equal("ครีมกันแดด", "หมวก");
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}