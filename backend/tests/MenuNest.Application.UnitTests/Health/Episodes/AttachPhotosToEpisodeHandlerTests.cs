using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.DrugMaster.AttachPhotosToDrug;
using MenuNest.Application.UseCases.Health.Episodes.AttachPhotosToEpisode;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Health.Episodes;

public class AttachPhotosToEpisodeHandlerTests
{
    [Fact]
    public async Task Creates_photo_rows_with_symptom_episode_parent_type()
    {
        using var fx = new HandlerTestFixture();
        var episode = SymptomEpisode.Start(fx.User.Id, Guid.NewGuid(), severity: 6);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        var sut = new AttachPhotosToEpisodeHandler(fx.Db, fx.UserProvisioner.Object);

        var photos = new[]
        {
            new AttachedPhotoInfo(
                "https://storage.blob.core.windows.net/episode-images/p1.jpg", 1234, "image/jpeg"),
            new AttachedPhotoInfo(
                "https://storage.blob.core.windows.net/episode-images/p2.png", 5678, "image/png"),
        };

        var result = await sut.Handle(
            new AttachPhotosToEpisodeCommand(episode.Id, photos),
            CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Url.Should().Be(photos[0].BlobUrl);
        result[0].FileSize.Should().Be(1234);
        result[1].ContentType.Should().Be("image/png");

        var persisted = fx.Db.Photos.Where(p => p.ParentId == episode.Id).ToList();
        persisted.Should().HaveCount(2);
        persisted.Should().AllSatisfy(p =>
        {
            p.ParentType.Should().Be(PhotoParentType.SymptomEpisode);
            p.UserId.Should().Be(fx.User.Id);
            p.ContainerName.Should().Be("episode-images");
            p.DeletedAt.Should().BeNull();
        });
    }

    [Fact]
    public async Task Throws_when_episode_not_found()
    {
        using var fx = new HandlerTestFixture();
        var sut = new AttachPhotosToEpisodeHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(
            new AttachPhotosToEpisodeCommand(
                Guid.NewGuid(),
                new[] { new AttachedPhotoInfo("https://x/p.jpg", 1, "image/jpeg") }),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Episode not found.");
    }

    [Fact]
    public async Task Throws_when_episode_owned_by_another_user()
    {
        using var fx = new HandlerTestFixture();
        var otherUserId = Guid.NewGuid();
        var foreignEpisode = SymptomEpisode.Start(otherUserId, Guid.NewGuid(), severity: 5);
        fx.Db.SymptomEpisodes.Add(foreignEpisode);
        await fx.Db.SaveChangesAsync();

        var sut = new AttachPhotosToEpisodeHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(
            new AttachPhotosToEpisodeCommand(
                foreignEpisode.Id,
                new[] { new AttachedPhotoInfo("https://x/p.jpg", 1, "image/jpeg") }),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Episode not found.");
        fx.Db.Photos.Any().Should().BeFalse();
    }

    [Fact]
    public async Task Throws_when_photos_list_empty()
    {
        using var fx = new HandlerTestFixture();
        var episode = SymptomEpisode.Start(fx.User.Id, Guid.NewGuid(), severity: 6);
        fx.Db.SymptomEpisodes.Add(episode);
        await fx.Db.SaveChangesAsync();

        var sut = new AttachPhotosToEpisodeHandler(fx.Db, fx.UserProvisioner.Object);

        var act = async () => await sut.Handle(
            new AttachPhotosToEpisodeCommand(episode.Id, Array.Empty<AttachedPhotoInfo>()),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("At least one photo is required.");
    }
}
