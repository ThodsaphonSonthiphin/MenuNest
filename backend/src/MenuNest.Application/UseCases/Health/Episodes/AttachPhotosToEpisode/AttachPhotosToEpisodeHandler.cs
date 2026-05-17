using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Episodes.AttachPhotosToEpisode;

public sealed class AttachPhotosToEpisodeHandler
    : ICommandHandler<AttachPhotosToEpisodeCommand, IReadOnlyList<PhotoRefDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public AttachPhotosToEpisodeHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<PhotoRefDto>> Handle(
        AttachPhotosToEpisodeCommand command, CancellationToken ct)
    {
        if (command.Photos.Count == 0)
            throw new DomainException("At least one photo is required.");

        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var episode = await _db.SymptomEpisodes
            .FirstOrDefaultAsync(e => e.Id == command.EpisodeId && e.UserId == user.Id, ct)
            ?? throw new DomainException("Episode not found.");

        var attached = new List<Photo>();
        foreach (var info in command.Photos)
        {
            var photo = Photo.Create(
                userId: user.Id,
                parentType: PhotoParentType.SymptomEpisode,
                parentId: episode.Id,
                blobUrl: info.BlobUrl,
                containerName: "episode-images",
                fileSize: info.FileSize,
                contentType: info.ContentType);
            _db.Photos.Add(photo);
            attached.Add(photo);
        }

        await _db.SaveChangesAsync(ct);

        return attached
            .Select(p => new PhotoRefDto(p.Id, p.BlobUrl, p.FileSize, p.ContentType))
            .ToList();
    }
}
