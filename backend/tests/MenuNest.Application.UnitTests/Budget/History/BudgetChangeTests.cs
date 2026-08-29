using FluentAssertions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Application.UnitTests.Budget.History;

public class BudgetChangeTests
{
    private static readonly Guid Fam = Guid.NewGuid();
    private static readonly Guid Usr = Guid.NewGuid();
    private static readonly Guid Cat = Guid.NewGuid();

    [Fact]
    public void RecordAssign_stores_the_delta_not_the_absolute_amount()
    {
        var c = BudgetChange.RecordAssign(Fam, Usr, 2026, 8, Cat, delta: 300m, batchId: null);

        c.Kind.Should().Be(BudgetChangeKind.Assign);
        c.Delta.Should().Be(300m);
        c.CategoryId.Should().Be(Cat);
        c.SecondCategoryId.Should().BeNull();
        c.IsUndone.Should().BeFalse();
    }

    [Fact]
    public void RecordAssign_rejects_a_zero_delta()
    {
        var act = () => BudgetChange.RecordAssign(Fam, Usr, 2026, 8, Cat, 0m, null);
        act.Should().Throw<DomainException>().WithMessage("*no effect*");
    }

    [Fact]
    public void MarkUndone_then_MarkRedone_returns_the_row_to_active()
    {
        var c = BudgetChange.RecordAssign(Fam, Usr, 2026, 8, Cat, 300m, null);
        var at = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);

        c.MarkUndone(Usr, at);
        c.IsUndone.Should().BeTrue();
        c.UndoneByUserId.Should().Be(Usr);
        c.UndoneAt.Should().Be(at);

        c.MarkRedone();
        c.IsUndone.Should().BeFalse();
        c.UndoneByUserId.Should().BeNull();
        c.UndoneAt.Should().BeNull();
    }

    [Fact]
    public void MarkUndone_twice_is_rejected()
    {
        var c = BudgetChange.RecordAssign(Fam, Usr, 2026, 8, Cat, 300m, null);
        c.MarkUndone(Usr, DateTime.UtcNow);

        var act = () => c.MarkUndone(Usr, DateTime.UtcNow);
        act.Should().Throw<DomainException>().WithMessage("*already undone*");
    }
}
