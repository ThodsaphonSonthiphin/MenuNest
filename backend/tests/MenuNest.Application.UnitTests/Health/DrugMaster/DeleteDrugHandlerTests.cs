using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Health.DrugMaster.DeleteDrug;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Health.DrugMaster;

public class DeleteDrugHandlerTests
{
    [Fact]
    public async Task Soft_deletes_drug_and_cascades_to_photos()
    {
        using var fx = new HandlerTestFixture();
        var drug = Drug.Create(fx.User.Id, "Para", DrugType.Analgesic, "500mg", 4, 6, 8);
        var photo1 = Photo.Create(fx.User.Id, PhotoParentType.Drug, drug.Id,
            "https://blob/1.jpg", "drug-images", 100, "image/jpeg");
        var photo2 = Photo.Create(fx.User.Id, PhotoParentType.Drug, drug.Id,
            "https://blob/2.jpg", "drug-images", 100, "image/jpeg");
        fx.Db.Drugs.Add(drug);
        fx.Db.Photos.AddRange(photo1, photo2);
        await fx.Db.SaveChangesAsync();

        var sut = new DeleteDrugHandler(fx.Db, fx.UserProvisioner.Object);
        await sut.Handle(new DeleteDrugCommand(drug.Id), CancellationToken.None);

        fx.Db.Drugs.Single(d => d.Id == drug.Id).DeletedAt.Should().NotBeNull();
        fx.Db.Photos.Where(p => p.ParentId == drug.Id).Should().AllSatisfy(p =>
            p.DeletedAt.Should().NotBeNull());
    }

    [Fact]
    public async Task Throws_when_drug_does_not_belong_to_current_user()
    {
        using var fx = new HandlerTestFixture();
        var otherUserId = Guid.NewGuid();
        var drug = Drug.Create(otherUserId, "Foreign", DrugType.Other, "1", 1, 2, 1);
        fx.Db.Drugs.Add(drug);
        await fx.Db.SaveChangesAsync();

        var sut = new DeleteDrugHandler(fx.Db, fx.UserProvisioner.Object);
        var act = async () => await sut.Handle(new DeleteDrugCommand(drug.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Drug not found.");
    }

    [Fact]
    public async Task Throws_when_drug_is_already_deleted()
    {
        using var fx = new HandlerTestFixture();
        var drug = Drug.Create(fx.User.Id, "Para", DrugType.Other, "1", 1, 2, 1);
        drug.SoftDelete();
        fx.Db.Drugs.Add(drug);
        await fx.Db.SaveChangesAsync();

        var sut = new DeleteDrugHandler(fx.Db, fx.UserProvisioner.Object);
        var act = async () => await sut.Handle(new DeleteDrugCommand(drug.Id), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }
}
