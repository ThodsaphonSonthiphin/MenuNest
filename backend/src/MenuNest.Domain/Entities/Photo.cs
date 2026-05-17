using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A photo attached to one of <see cref="PhotoParentType.Drug"/>,
/// <see cref="PhotoParentType.SymptomEpisode"/>, or <see cref="PhotoParentType.Intake"/>.
/// Uses a discriminator (<see cref="ParentType"/> + <see cref="ParentId"/>)
/// instead of three FK columns so adding more parent types later is purely
/// additive.
/// </summary>
public sealed class Photo : Entity
{
    public Guid UserId { get; private set; }
    public PhotoParentType ParentType { get; private set; }
    public Guid ParentId { get; private set; }
    public string BlobUrl { get; private set; } = null!;
    public string ContainerName { get; private set; } = null!;
    public long FileSize { get; private set; }
    public string ContentType { get; private set; } = null!;
    public DateTime? DeletedAt { get; private set; }

    // EF Core
    private Photo() { }

    public static Photo Create(
        Guid userId,
        PhotoParentType parentType,
        Guid parentId,
        string blobUrl,
        string containerName,
        long fileSize,
        string contentType)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");
        if (parentId == Guid.Empty)
            throw new DomainException("ParentId is required.");
        if (string.IsNullOrWhiteSpace(blobUrl))
            throw new DomainException("Blob URL is required.");
        if (string.IsNullOrWhiteSpace(containerName))
            throw new DomainException("Container name is required.");
        if (fileSize <= 0)
            throw new DomainException("File size must be positive.");
        if (string.IsNullOrWhiteSpace(contentType))
            throw new DomainException("Content type is required.");

        return new Photo
        {
            UserId = userId,
            ParentType = parentType,
            ParentId = parentId,
            BlobUrl = blobUrl.Trim(),
            ContainerName = containerName.Trim(),
            FileSize = fileSize,
            ContentType = contentType.Trim()
        };
    }

    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
