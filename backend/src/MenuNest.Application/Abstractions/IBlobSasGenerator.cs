namespace MenuNest.Application.Abstractions;

/// <summary>
/// Generates short-lived, path-scoped, user-delegation SAS URLs for direct
/// browser-to-blob uploads and reads. Implementation in Infrastructure uses
/// <c>DefaultAzureCredential</c> + <c>GetUserDelegationKey</c> so no storage
/// account key is ever in memory.
/// </summary>
public interface IBlobSasGenerator
{
    /// <summary>
    /// Returns a write-only SAS URL scoped to a single blob path under
    /// <paramref name="container"/>. Blob path is composed of
    /// <c>user-{userId}/{parentType}-{parentId}/{Guid}.{ext}</c> so the
    /// SAS holder cannot write outside their own scope.
    /// </summary>
    Task<BlobUploadSas> GenerateUploadSasAsync(
        string container,
        Guid userId,
        string parentType,
        Guid parentId,
        string contentType,
        TimeSpan validity,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a read-only SAS URL for an existing blob, with the given
    /// validity. Used to render photos in the doctor report and in-app views.
    /// </summary>
    Task<string> GenerateReadSasAsync(
        string blobUrl,
        TimeSpan validity,
        CancellationToken ct = default);
}

/// <summary>
/// Payload returned by <see cref="IBlobSasGenerator.GenerateUploadSasAsync"/>.
/// </summary>
/// <param name="UploadUrl">URL the client PUTs to (includes SAS query).</param>
/// <param name="BlobUrl">Plain URL (no SAS) to persist on the parent entity.</param>
public sealed record BlobUploadSas(string UploadUrl, string BlobUrl);
