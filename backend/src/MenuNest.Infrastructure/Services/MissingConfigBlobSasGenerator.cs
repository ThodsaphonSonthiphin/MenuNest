using MenuNest.Application.Abstractions;

namespace MenuNest.Infrastructure.Services;

/// <summary>
/// Stub <see cref="IBlobSasGenerator"/> registered when
/// <c>Storage:BlobEndpoint</c> is absent from configuration (typically
/// local dev without Azure Storage emulator). Resolving the service still
/// succeeds so DI bootstrap works, but any method invocation throws
/// <see cref="InvalidOperationException"/> with a clear message pointing
/// at the missing setting.
/// </summary>
internal sealed class MissingConfigBlobSasGenerator : IBlobSasGenerator
{
    private const string Message =
        "Storage:BlobEndpoint is not configured. Set it in appsettings.json " +
        "(e.g., \"https://<account>.blob.core.windows.net/\") to enable photo uploads.";

    public Task<BlobUploadSas> GenerateUploadSasAsync(
        string container,
        Guid userId,
        string parentType,
        Guid parentId,
        string contentType,
        TimeSpan validity,
        CancellationToken ct = default)
        => throw new InvalidOperationException(Message);

    public Task<string> GenerateReadSasAsync(string blobUrl, TimeSpan validity, CancellationToken ct = default)
        => throw new InvalidOperationException(Message);
}
