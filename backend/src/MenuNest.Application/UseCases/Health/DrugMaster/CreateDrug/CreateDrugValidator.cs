using FluentValidation;

namespace MenuNest.Application.UseCases.Health.DrugMaster.CreateDrug;

public sealed class CreateDrugValidator : AbstractValidator<CreateDrugCommand>
{
    public CreateDrugValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DoseStrength).NotEmpty().MaximumLength(50);
        RuleFor(x => x.EffectDurationMinHours).GreaterThan(0);
        RuleFor(x => x.EffectDurationMaxHours).GreaterThanOrEqualTo(x => x.EffectDurationMinHours);
        RuleFor(x => x.MaxDailyDose).GreaterThan(0);
        RuleFor(x => x.StockCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.UsageNote).MaximumLength(1000);
        RuleFor(x => x.ActiveIngredient).MaximumLength(200);
    }
}
