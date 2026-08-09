using FluentValidation;
using Mediator;
using MenuNest.Application.UseCases.Trips;

namespace MenuNest.Application.UseCases.Trips.SetChecklistEntryChecked;

public sealed record SetChecklistEntryCheckedCommand(Guid TripId, Guid StopId, Guid EntryId, bool IsChecked) : ICommand<StopChecklistEntryDto>;

public sealed class SetChecklistEntryCheckedValidator : AbstractValidator<SetChecklistEntryCheckedCommand>
{
    public SetChecklistEntryCheckedValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.StopId).NotEmpty();
        RuleFor(x => x.EntryId).NotEmpty();
    }
}