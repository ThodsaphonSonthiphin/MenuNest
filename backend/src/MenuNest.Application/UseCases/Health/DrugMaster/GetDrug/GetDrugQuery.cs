using Mediator;

namespace MenuNest.Application.UseCases.Health.DrugMaster.GetDrug;

public sealed record GetDrugQuery(Guid Id) : IQuery<DrugDetailDto>;
