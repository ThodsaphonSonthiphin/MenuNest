using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.Symptoms.CreateCustomSymptom;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Health.Symptoms;

public class CreateCustomSymptomHandlerTests
{
    [Fact]
    public async Task Creates_user_scoped_symptom()
    {
        using var fx = new HandlerTestFixture();
        var sut = new CreateCustomSymptomHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(
            new CreateCustomSymptomCommand("ปวดเอว"),
            CancellationToken.None);

        result.Name.Should().Be("ปวดเอว");
        result.IsSeed.Should().BeFalse();

        var persisted = fx.Db.Symptoms.Single(s => s.Id == result.Id);
        persisted.UserId.Should().Be(fx.User.Id);
        persisted.IsSeed.Should().BeFalse();
    }

    [Fact]
    public async Task Trims_whitespace_from_name()
    {
        using var fx = new HandlerTestFixture();
        var sut = new CreateCustomSymptomHandler(fx.Db, fx.UserProvisioner.Object);

        var result = await sut.Handle(
            new CreateCustomSymptomCommand("  ปวดข้อนิ้ว  "),
            CancellationToken.None);

        result.Name.Should().Be("ปวดข้อนิ้ว");
    }

    [Fact]
    public async Task Rejects_name_that_clashes_with_a_seed()
    {
        using var fx = new HandlerTestFixture();
        fx.Db.Symptoms.Add(Symptom.CreateSeed("ปวดหัว"));
        await fx.Db.SaveChangesAsync();

        var sut = new CreateCustomSymptomHandler(fx.Db, fx.UserProvisioner.Object);
        var act = async () => await sut.Handle(
            new CreateCustomSymptomCommand("ปวดหัว"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("A symptom with this name already exists.");
    }

    [Fact]
    public async Task Rejects_duplicate_user_symptom()
    {
        using var fx = new HandlerTestFixture();
        fx.Db.Symptoms.Add(Symptom.CreateCustom("ปวดเอว", fx.User.Id));
        await fx.Db.SaveChangesAsync();

        var sut = new CreateCustomSymptomHandler(fx.Db, fx.UserProvisioner.Object);
        var act = async () => await sut.Handle(
            new CreateCustomSymptomCommand("ปวดเอว"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Allows_same_name_for_different_users()
    {
        using var fx = new HandlerTestFixture();
        var otherUserId = Guid.NewGuid();
        fx.Db.Symptoms.Add(Symptom.CreateCustom("ปวดสะโพก", otherUserId));
        await fx.Db.SaveChangesAsync();

        var sut = new CreateCustomSymptomHandler(fx.Db, fx.UserProvisioner.Object);
        var result = await sut.Handle(
            new CreateCustomSymptomCommand("ปวดสะโพก"),
            CancellationToken.None);

        result.Name.Should().Be("ปวดสะโพก");
    }
}
