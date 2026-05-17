using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UseCases.Health.DrugMaster.CreateDrug;

public sealed class CreateDrugHandler : ICommandHandler<CreateDrugCommand, DrugDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<CreateDrugCommand> _validator;

    public CreateDrugHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<CreateDrugCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<DrugDetailDto> Handle(CreateDrugCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var drug = Drug.Create(
            userId: user.Id,
            name: command.Name,
            drugType: command.DrugType,
            doseStrength: command.DoseStrength,
            effectDurationMinHours: command.EffectDurationMinHours,
            effectDurationMaxHours: command.EffectDurationMaxHours,
            maxDailyDose: command.MaxDailyDose,
            stockCount: command.StockCount,
            activeIngredient: command.ActiveIngredient,
            expirationDate: command.ExpirationDate,
            usageNote: command.UsageNote,
            treatsSymptomIds: command.TreatsSymptomIds);

        _db.Drugs.Add(drug);
        await _db.SaveChangesAsync(ct);

        // Photos are attached separately via AttachPhotosToDrug after the
        // client completes its blob upload and knows fileSize + contentType.
        return new DrugDetailDto(
            drug.Id, drug.Name, drug.ActiveIngredient, drug.DrugType, drug.DoseStrength,
            drug.EffectDurationMinHours, drug.EffectDurationMaxHours,
            drug.MaxDailyDose, drug.StockCount, drug.ExpirationDate, drug.UsageNote,
            drug.TreatsSymptomIds, Photos: Array.Empty<PhotoRefDto>(),
            drug.CreatedAt, drug.UpdatedAt);
    }
}
