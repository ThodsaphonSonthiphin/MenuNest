using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.Photos.RequestUploadSas;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Moq;

namespace MenuNest.Application.UnitTests.Health.Photos;

public class RequestUploadSasHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Generates_sas_for_drug_when_caller_owns_drug()
    {
        using var fx = new HandlerTestFixture();
        var drug = Drug.Create(fx.User.Id, "Paracetamol", DrugType.Analgesic, "500mg", 4, 6, 8);
        fx.Db.Drugs.Add(drug);
        await fx.Db.SaveChangesAsync();

        var sasMock = new Mock<IBlobSasGenerator>();
        sasMock
            .Setup(s => s.GenerateUploadSasAsync(
                "drug-images",
                fx.User.Id,
                "drug",
                drug.Id,
                "image/jpeg",
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlobUploadSas(
                UploadUrl: "https://storage.blob.core.windows.net/drug-images/path?sig=fake",
                BlobUrl: "https://storage.blob.core.windows.net/drug-images/path"));

        var clock = new FixedClock(FixedNow);
        var sut = new RequestUploadSasHandler(fx.Db, fx.UserProvisioner.Object, sasMock.Object, clock);

        var result = await sut.Handle(
            new RequestUploadSasCommand("drug", drug.Id, "image/jpeg"),
            CancellationToken.None);

        result.UploadUrl.Should().Contain("sig=fake");
        result.BlobUrl.Should().NotContain("sig=");
        result.ExpiresAt.Should().Be(FixedNow.AddMinutes(15));

        sasMock.Verify(s => s.GenerateUploadSasAsync(
            "drug-images", fx.User.Id, "drug", drug.Id, "image/jpeg",
            TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Generates_sas_for_episode_when_caller_owns_episode()
    {
        using var fx = new HandlerTestFixture();
        var episode = SymptomEpisode.Start(fx.User.Id, Guid.NewGuid(), severity: 6);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        var sasMock = new Mock<IBlobSasGenerator>();
        sasMock
            .Setup(s => s.GenerateUploadSasAsync(
                "episode-images",
                fx.User.Id,
                "episode",
                episode.Id,
                "image/png",
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlobUploadSas(
                UploadUrl: "https://storage.blob.core.windows.net/episode-images/path?sig=fake",
                BlobUrl: "https://storage.blob.core.windows.net/episode-images/path"));

        var sut = new RequestUploadSasHandler(
            fx.Db, fx.UserProvisioner.Object, sasMock.Object, new FixedClock(FixedNow));

        var result = await sut.Handle(
            new RequestUploadSasCommand("episode", episode.Id, "image/png"),
            CancellationToken.None);

        result.UploadUrl.Should().Contain("episode-images");
        sasMock.Verify(s => s.GenerateUploadSasAsync(
            "episode-images", fx.User.Id, "episode", episode.Id, "image/png",
            TimeSpan.FromMinutes(15), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Throws_when_drug_not_found()
    {
        using var fx = new HandlerTestFixture();
        var sasMock = new Mock<IBlobSasGenerator>();
        var sut = new RequestUploadSasHandler(
            fx.Db, fx.UserProvisioner.Object, sasMock.Object, new FixedClock(FixedNow));

        var act = async () => await sut.Handle(
            new RequestUploadSasCommand("drug", Guid.NewGuid(), "image/jpeg"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Drug not found.");
        sasMock.Verify(s => s.GenerateUploadSasAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Throws_when_episode_not_found()
    {
        using var fx = new HandlerTestFixture();
        var sasMock = new Mock<IBlobSasGenerator>();
        var sut = new RequestUploadSasHandler(
            fx.Db, fx.UserProvisioner.Object, sasMock.Object, new FixedClock(FixedNow));

        var act = async () => await sut.Handle(
            new RequestUploadSasCommand("episode", Guid.NewGuid(), "image/jpeg"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Episode not found.");
        sasMock.Verify(s => s.GenerateUploadSasAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Throws_when_drug_belongs_to_another_user()
    {
        using var fx = new HandlerTestFixture();
        // Drug created with a different user id than the fixture's User.
        var otherUserId = Guid.NewGuid();
        var foreignDrug = Drug.Create(otherUserId, "Other", DrugType.Other, "100mg", 2, 4, 4);
        fx.Db.Drugs.Add(foreignDrug);
        await fx.Db.SaveChangesAsync();

        var sasMock = new Mock<IBlobSasGenerator>();
        var sut = new RequestUploadSasHandler(
            fx.Db, fx.UserProvisioner.Object, sasMock.Object, new FixedClock(FixedNow));

        var act = async () => await sut.Handle(
            new RequestUploadSasCommand("drug", foreignDrug.Id, "image/jpeg"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Drug not found.");
        sasMock.Verify(s => s.GenerateUploadSasAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Throws_when_episode_belongs_to_another_user()
    {
        using var fx = new HandlerTestFixture();
        var otherUserId = Guid.NewGuid();
        var foreignEpisode = SymptomEpisode.Start(otherUserId, Guid.NewGuid(), severity: 5);
        fx.Db.SymptomEpisodes.Add(foreignEpisode);
        await fx.Db.SaveChangesAsync();

        var sasMock = new Mock<IBlobSasGenerator>();
        var sut = new RequestUploadSasHandler(
            fx.Db, fx.UserProvisioner.Object, sasMock.Object, new FixedClock(FixedNow));

        var act = async () => await sut.Handle(
            new RequestUploadSasCommand("episode", foreignEpisode.Id, "image/jpeg"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Episode not found.");
        sasMock.Verify(s => s.GenerateUploadSasAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("intake")]
    [InlineData("Drugs")]
    [InlineData("photo")]
    [InlineData("")]
    public async Task Throws_when_container_key_is_not_allowed(string containerKey)
    {
        using var fx = new HandlerTestFixture();
        var sasMock = new Mock<IBlobSasGenerator>();
        var sut = new RequestUploadSasHandler(
            fx.Db, fx.UserProvisioner.Object, sasMock.Object, new FixedClock(FixedNow));

        var act = async () => await sut.Handle(
            new RequestUploadSasCommand(containerKey, Guid.NewGuid(), "image/jpeg"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.Message.StartsWith("Invalid container key"));
    }

    [Fact]
    public async Task Throws_when_content_type_is_empty()
    {
        using var fx = new HandlerTestFixture();
        var drug = Drug.Create(fx.User.Id, "Paracetamol", DrugType.Analgesic, "500mg", 4, 6, 8);
        fx.Db.Drugs.Add(drug);
        await fx.Db.SaveChangesAsync();

        var sasMock = new Mock<IBlobSasGenerator>();
        var sut = new RequestUploadSasHandler(
            fx.Db, fx.UserProvisioner.Object, sasMock.Object, new FixedClock(FixedNow));

        var act = async () => await sut.Handle(
            new RequestUploadSasCommand("drug", drug.Id, "  "),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Content type is required.");
    }

    [Fact]
    public async Task Throws_when_parent_id_is_empty()
    {
        using var fx = new HandlerTestFixture();
        var sasMock = new Mock<IBlobSasGenerator>();
        var sut = new RequestUploadSasHandler(
            fx.Db, fx.UserProvisioner.Object, sasMock.Object, new FixedClock(FixedNow));

        var act = async () => await sut.Handle(
            new RequestUploadSasCommand("drug", Guid.Empty, "image/jpeg"),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("ParentId is required.");
    }
}
