using Mediator;

namespace MenuNest.Application.UseCases.Health.Triggers.CreateCustomTrigger;

public sealed record CreateCustomTriggerCommand(string Name) : ICommand<TriggerDto>;
