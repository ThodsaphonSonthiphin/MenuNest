using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.DrugMaster.AttachPhotosToDrug;

public sealed class AttachPhotosToDrugHandler
    : ICommandHandler<AttachPhotosToDrugCommand, IReadOnlyList<PhotoRefDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public AttachPhotosToDrugHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<PhotoRefDto>> Handle(
        AttachPhotosToDrugCommand command, CancellationToken ct)
    {
        if (command.Photos.Count == 0)
            throw new DomainException("At least one photo is required.");

        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var drug = await _db.Drugs
            .FirstOrDefaultAsync(d =>
                d.Id == command.DrugId && d.UserId == user.Id && d.DeletedAt == null, ct)
            ?? throw new DomainException("Drug not found.");

        var attached = new List<Photo>();
        foreach (var info in command.Photos)
        {
            var photo = Photo.Create(
                userId: user.Id,
                parentType: PhotoParentType.Drug,
                parentId: drug.Id,
                blobUrl: info.BlobUrl,
                containerName: "drug-images",
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
