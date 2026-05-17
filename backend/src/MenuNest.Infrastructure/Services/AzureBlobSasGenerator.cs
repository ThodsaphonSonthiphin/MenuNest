using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using MenuNest.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace MenuNest.Infrastructure.Services;

/// <summary>
/// Real <see cref="IBlobSasGenerator"/> implementation backed by Azure Blob
/// Storage. Uses a user-delegation key derived from <c>DefaultAzureCredential</c>
/// (UAMI in production, developer credential locally) so no account key is ever
/// in memory. This is required because the storage account is provisioned with
/// <c>allowSharedKeyAccess: false</c>.
///
/// Upload SAS URLs are scoped to a single blob path under
/// <c>user-{userId}/{parentType}-{parentId}/{guid}.{ext}</c>, which means a
/// leaked SAS can only ever write to that single blob (never overwrite another
/// user's photos or escape the per-entity prefix).
/// </summary>
internal sealed class AzureBlobSasGenerator : IBlobSasGenerator
{
    private readonly BlobServiceClient _blobService;
    private readonly ILogger<AzureBlobSasGenerator> _logger;

    public AzureBlobSasGenerator(BlobServiceClient blobService, ILogger<AzureBlobSasGenerator> logger)
    {
        _blobService = blobService;
        _logger = logger;
    }

    public async Task<BlobUploadSas> GenerateUploadSasAsync(
        string container,
        Guid userId,
        string parentType,
        Guid parentId,
        string contentType,
        TimeSpan validity,
        CancellationToken ct = default)
    {
        // Path-scoped: user-{userId}/{parentType}-{parentId}/{guid}.{ext}
        // Single-blob (Resource = "b") SAS means even a leaked URL cannot
        // be used to upload to a different blob within the container.
        var ext = ContentTypeToExt(contentType);
        var blobName = $"user-{userId:N}/{parentType.ToLowerInvariant()}-{parentId:N}/{Guid.NewGuid():N}.{ext}";

        var containerClient = _blobService.GetBlobContainerClient(container);
        var blobClient = containerClient.GetBlobClient(blobName);

        // User-delegation key avoids the storage account key — required because
        // the storage account is provisioned with allowSharedKeyAccess=false.
        var nowUtc = DateTimeOffset.UtcNow;
        var expiresOn = nowUtc.Add(validity);
        var userDelegationKey = await _blobService.GetUserDelegationKeyAsync(
            startsOn: nowUtc.AddMinutes(-5),
            expiresOn: expiresOn,
            cancellationToken: ct);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = container,
            BlobName = blobName,
            Resource = "b",
            StartsOn = nowUtc.AddMinutes(-5),
            ExpiresOn = expiresOn,
            Protocol = SasProtocol.Https,
            ContentType = contentType,
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);

        var sas = sasBuilder.ToSasQueryParameters(
            userDelegationKey.Value,
            _blobService.AccountName);

        var uploadUrl = new BlobUriBuilder(blobClient.Uri) { Sas = sas }.ToUri().ToString();
        var blobUrl = blobClient.Uri.ToString(); // no SAS, for persistence on the parent entity

        _logger.LogDebug(
            "Generated upload SAS for user {UserId} parent {ParentType}/{ParentId} → {BlobName}",
            userId, parentType, parentId, blobName);

        return new BlobUploadSas(uploadUrl, blobUrl);
    }

    public async Task<string> GenerateReadSasAsync(string blobUrl, TimeSpan validity, CancellationToken ct = default)
    {
        var uri = new Uri(blobUrl);
        var builder = new BlobUriBuilder(uri);
        var nowUtc = DateTimeOffset.UtcNow;
        var expiresOn = nowUtc.Add(validity);

        var userDelegationKey = await _blobService.GetUserDelegationKeyAsync(
            startsOn: nowUtc.AddMinutes(-5),
            expiresOn: expiresOn,
            cancellationToken: ct);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = builder.BlobContainerName,
            BlobName = builder.BlobName,
            Resource = "b",
            StartsOn = nowUtc.AddMinutes(-5),
            ExpiresOn = expiresOn,
            Protocol = SasProtocol.Https,
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sas = sasBuilder.ToSasQueryParameters(userDelegationKey.Value, _blobService.AccountName);
        builder.Sas = sas;
        return builder.ToUri().ToString();
    }

    private static string ContentTypeToExt(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => "jpg",
        "image/jpg" => "jpg",
        "image/png" => "png",
        "image/webp" => "webp",
        "image/gif" => "gif",
        _ => "bin"
    };
}
