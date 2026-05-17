using Mediator;

namespace MenuNest.Application.UseCases.Health.Triggers.ListTriggers;

public sealed record ListTriggersQuery : IQuery<IReadOnlyList<TriggerDto>>;
