using FluentValidation;

namespace MenuNest.Application.UseCases.Health.PushSubscriptions.SubscribeWebPush;

public sealed class SubscribeWebPushValidator : AbstractValidator<SubscribeWebPushCommand>
{
    public SubscribeWebPushValidator()
    {
        RuleFor(x => x.Endpoint).NotEmpty();
        RuleFor(x => x.P256dh).NotEmpty();
        RuleFor(x => x.Auth).NotEmpty();
    }
}
