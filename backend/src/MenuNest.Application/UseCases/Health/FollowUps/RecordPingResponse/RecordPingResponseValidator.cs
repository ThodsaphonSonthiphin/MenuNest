using FluentValidation;

namespace MenuNest.Application.UseCases.Health.FollowUps.RecordPingResponse;

public sealed class RecordPingResponseValidator : AbstractValidator<RecordPingResponseCommand>
{
    public RecordPingResponseValidator()
    {
        RuleFor(x => x.PingId).NotEmpty();
        RuleFor(x => x.SeverityAtCheck)
            .InclusiveBetween(0, 10)
            .When(x => x.SeverityAtCheck.HasValue);
    }
}
