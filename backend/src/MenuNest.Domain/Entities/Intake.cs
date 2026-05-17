using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A single medication dose taken by the user. Optionally linked to the
/// <see cref="SymptomEpisode"/> that prompted it — the link is what
/// enables doctor-report treatment-efficacy analysis (drug X resolved
/// episodes N times in M minutes).
/// </summary>
public sealed class Intake : Entity
{
    public Guid UserId { get; private set; }
    public Guid DrugId { get; private set; }
    public Guid? SymptomEpisodeId { get; private set; }
    public DateTime TakenAt { get; private set; }
    public int DoseAmount { get; private set; }
    public string? Notes { get; private set; }

    // EF Core
    private Intake() { }

    public static Intake Create(
        Guid userId,
        Guid drugId,
        int doseAmount,
        Guid? symptomEpisodeId = null,
        DateTime? takenAt = null,
        string? notes = null)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");
        if (drugId == Guid.Empty)
            throw new DomainException("DrugId is required.");
        if (doseAmount <= 0)
            throw new DomainException("Dose amount must be positive.");

        return new Intake
        {
            UserId = userId,
            DrugId = drugId,
            SymptomEpisodeId = symptomEpisodeId,
            DoseAmount = doseAmount,
            TakenAt = takenAt ?? DateTime.UtcNow,
            Notes = notes?.Trim()
        };
    }

    public void UpdateNotes(string? notes)
    {
        Notes = notes?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
