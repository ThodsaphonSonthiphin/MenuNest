using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Health.DrugMaster.UpdateDrug;

public sealed class UpdateDrugHandler : ICommandHandler<UpdateDrugCommand, DrugDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<UpdateDrugCommand> _validator;

    public UpdateDrugHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<UpdateDrugCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<DrugDetailDto> Handle(UpdateDrugCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var drug = await _db.Drugs
            .FirstOrDefaultAsync(d => d.Id == command.Id && d.UserId == user.Id && d.DeletedAt == null, ct)
            ?? throw new DomainException("Drug not found.");

        drug.UpdateProfile(
            name: command.Name,
            drugType: command.DrugType,
            doseStrength: command.DoseStrength,
            effectDurationMinHours: command.EffectDurationMinHours,
            effectDurationMaxHours: command.EffectDurationMaxHours,
            maxDailyDose: command.MaxDailyDose,
            activeIngredient: command.ActiveIngredient,
            expirationDate: command.ExpirationDate,
            usageNote: command.UsageNote);

        // Stock is updated separately because it has its own bounds check.
        drug.UpdateStock(command.StockCount);

        if (command.TreatsSymptomIds is not null)
        {
            drug.SetTreats(command.TreatsSymptomIds);
        }

        await _db.SaveChangesAsync(ct);

        var photos = await _db.Photos
            .Where(p => p.ParentType == PhotoParentType.Drug && p.ParentId == drug.Id && p.DeletedAt == null)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PhotoRefDto(p.Id, p.BlobUrl, p.FileSize, p.ContentType))
            .ToListAsync(ct);

        return new DrugDetailDto(
            drug.Id, drug.Name, drug.ActiveIngredient, drug.DrugType, drug.DoseStrength,
            drug.EffectDurationMinHours, drug.EffectDurationMaxHours,
            drug.MaxDailyDose, drug.StockCount, drug.ExpirationDate, drug.UsageNote,
            drug.TreatsSymptomIds, photos,
            drug.CreatedAt, drug.UpdatedAt);
    }
}
