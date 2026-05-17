using Mediator;

namespace MenuNest.Application.UseCases.Health.DrugMaster.AttachPhotosToDrug;

/// <param name="DrugId">Target drug.</param>
/// <param name="Photos">Per-photo metadata captured by the client after a
/// successful direct-to-blob upload via the SAS endpoint.</param>
public sealed record AttachPhotosToDrugCommand(
    Guid DrugId,
    IReadOnlyList<AttachedPhotoInfo> Photos) : ICommand<IReadOnlyList<PhotoRefDto>>;

public sealed record AttachedPhotoInfo(
    string BlobUrl,
    long FileSize,
    string ContentType);
