using Mediator;

namespace MenuNest.Application.UseCases.Health.Symptoms.CreateCustomSymptom;

public sealed record CreateCustomSymptomCommand(string Name) : ICommand<SymptomDto>;
