using Mediator;
using MenuNest.Application.UseCases.Health.DrugMaster.AttachPhotosToDrug;

namespace MenuNest.Application.UseCases.Health.Episodes.AttachPhotosToEpisode;

/// <summary>
/// Attaches one or more previously-uploaded photos (via the SAS flow) to a
/// SymptomEpisode owned by the current user. Mirror of
/// <see cref="AttachPhotosToDrugCommand"/> but with
/// <see cref="MenuNest.Domain.Enums.PhotoParentType.SymptomEpisode"/> parent type.
/// </summary>
/// <param name="EpisodeId">Target episode.</param>
/// <param name="Photos">Per-photo metadata captured by the client after a
/// successful direct-to-blob upload via the SAS endpoint.</param>
public sealed record AttachPhotosToEpisodeCommand(
    Guid EpisodeId,
    IReadOnlyList<AttachedPhotoInfo> Photos) : ICommand<IReadOnlyList<PhotoRefDto>>;
