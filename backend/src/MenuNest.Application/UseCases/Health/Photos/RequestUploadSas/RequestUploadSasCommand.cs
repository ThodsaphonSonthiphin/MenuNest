using Mediator;

namespace MenuNest.Application.UseCases.Health.Photos.RequestUploadSas;

/// <summary>
/// Asks the backend for a short-lived, single-blob upload SAS URL the
/// browser can <c>PUT</c> the photo bytes to directly. The frontend then
/// posts the resulting <c>blobUrl</c> back to the parent entity's
/// <c>/photos</c> endpoint to attach it.
/// </summary>
/// <param name="ContainerKey">
/// One of <c>"drug"</c> or <c>"episode"</c>. The handler maps each to the
/// matching container (<c>drug-images</c> / <c>episode-images</c>) and
/// verifies the parent is owned by the current user.
/// </param>
/// <param name="ParentId">ID of the Drug or SymptomEpisode the photo will be attached to.</param>
/// <param name="ContentType">MIME type of the upload (e.g., <c>image/jpeg</c>).</param>
public sealed record RequestUploadSasCommand(
    string ContainerKey,
    Guid ParentId,
    string ContentType) : ICommand<UploadSasResponse>;

/// <summary>
/// Response from <see cref="RequestUploadSasCommand"/>. <c>UploadUrl</c>
/// contains the SAS query string and is what the browser PUTs to.
/// <c>BlobUrl</c> is plain (no SAS) and is the value the client passes
/// back when attaching the photo to its parent entity.
/// </summary>
public sealed record UploadSasResponse(
    string UploadUrl,
    string BlobUrl,
    DateTime ExpiresAt);
