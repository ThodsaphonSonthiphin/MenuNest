using Mediator;
using FluentValidation;

namespace MenuNest.Application.UseCases.Trips.DetachChecklistItem;

public sealed record DetachChecklistItemCommand(Guid TripId, Guid StopId, Guid EntryId) : ICommand<bool>;

public sealed class DetachChecklistItemValidator : AbstractValidator<DetachChecklistItemCommand>
{
    public DetachChecklistItemValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.StopId).NotEmpty();
        RuleFor(x => x.EntryId).NotEmpty();
    }
}