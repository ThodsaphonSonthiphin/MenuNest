using Mediator;

namespace MenuNest.Application.UseCases.Health.DrugMaster.ListDrugs;

/// <param name="SymptomId">Optional filter — return only drugs that treat this symptom.</param>
public sealed record ListDrugsQuery(Guid? SymptomId = null)
    : IQuery<IReadOnlyList<DrugDto>>;
