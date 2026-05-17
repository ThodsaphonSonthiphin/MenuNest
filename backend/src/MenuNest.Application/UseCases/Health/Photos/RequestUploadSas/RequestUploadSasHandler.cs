using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.Photos.RequestUploadSas;

/// <summary>
/// Issues a 15-minute, write-only, single-blob SAS URL the browser uses to
/// upload a photo directly to Azure Blob Storage. Verifies the caller owns
/// the parent entity (Drug or SymptomEpisode) before generating the SAS so
/// an attacker cannot request a SAS scoped under another user's path.
/// </summary>
public sealed class RequestUploadSasHandler : ICommandHandler<RequestUploadSasCommand, UploadSasResponse>
{
    /// <summary>SAS validity. Aligns with the doctor report decision in the plan (Photo: 15 min).</summary>
    private static readonly TimeSpan UploadSasValidity = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Allowed values for <see cref="RequestUploadSasCommand.ContainerKey"/>,
    /// mapped to the storage container name provisioned by Bicep. <c>intake</c>
    /// is intentionally absent for now — Intake photos can be added later
    /// without touching the SAS contract.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ContainerMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["drug"] = "drug-images",
            ["episode"] = "episode-images",
        };

    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IBlobSasGenerator _sasGenerator;
    private readonly IClock _clock;

    public RequestUploadSasHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IBlobSasGenerator sasGenerator,
        IClock clock)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _sasGenerator = sasGenerator;
        _clock = clock;
    }

    public async ValueTask<UploadSasResponse> Handle(RequestUploadSasCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.ContentType))
            throw new DomainException("Content type is required.");
        if (command.ParentId == Guid.Empty)
            throw new DomainException("ParentId is required.");
        if (!ContainerMap.TryGetValue(command.ContainerKey ?? string.Empty, out var container))
            throw new DomainException(
                $"Invalid container key '{command.ContainerKey}'. Allowed: drug, episode.");

        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        // Verify the parent exists AND belongs to the current user. This is
        // the security boundary: without it, a caller could request a SAS
        // scoped under another user's path prefix.
        await EnsureParentOwnedAsync(command.ContainerKey!, command.ParentId, user.Id, ct);

        var sas = await _sasGenerator.GenerateUploadSasAsync(
            container: container,
            userId: user.Id,
            parentType: command.ContainerKey!,
            parentId: command.ParentId,
            contentType: command.ContentType,
            validity: UploadSasValidity,
            ct: ct);

        var expiresAt = _clock.UtcNow.Add(UploadSasValidity);
        return new UploadSasResponse(sas.UploadUrl, sas.BlobUrl, expiresAt);
    }

    private async Task EnsureParentOwnedAsync(string containerKey, Guid parentId, Guid userId, CancellationToken ct)
    {
        if (string.Equals(containerKey, "drug", StringComparison.OrdinalIgnoreCase))
        {
            var exists = await _db.Drugs.AnyAsync(
                d => d.Id == parentId && d.UserId == userId && d.DeletedAt == null, ct);
            if (!exists)
                throw new DomainException("Drug not found.");
        }
        else if (string.Equals(containerKey, "episode", StringComparison.OrdinalIgnoreCase))
        {
            var exists = await _db.SymptomEpisodes.AnyAsync(
                e => e.Id == parentId && e.UserId == userId, ct);
            if (!exists)
                throw new DomainException("Episode not found.");
        }
        else
        {
            // Unreachable: ContainerMap already filtered. Defense-in-depth only.
            throw new DomainException($"Unsupported container key '{containerKey}'.");
        }
    }
}
