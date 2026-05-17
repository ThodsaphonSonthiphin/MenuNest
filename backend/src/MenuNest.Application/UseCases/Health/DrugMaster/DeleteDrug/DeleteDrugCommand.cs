using Mediator;

namespace MenuNest.Application.UseCases.Health.DrugMaster.DeleteDrug;

public sealed record DeleteDrugCommand(Guid Id) : ICommand;
