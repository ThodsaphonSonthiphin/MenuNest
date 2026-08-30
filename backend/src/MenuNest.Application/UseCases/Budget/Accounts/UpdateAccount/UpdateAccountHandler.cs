using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Monthly;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Accounts.UpdateAccount;

public sealed class UpdateAccountHandler : ICommandHandler<UpdateAccountCommand, BudgetAccountDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<UpdateAccountCommand> _v;
    public UpdateAccountHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<UpdateAccountCommand> v)
    { _db = db; _users = users; _v = v; }

    public async ValueTask<BudgetAccountDto> Handle(UpdateAccountCommand c, CancellationToken ct)
    {
        await _v.ValidateAndThrowAsync(c, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);
        var acc = await _db.BudgetAccounts.FirstOrDefaultAsync(a => a.Id == c.Id && a.FamilyId == familyId, ct)
                  ?? throw new DomainException("Account not found.");

        // null for a Cash account — every write below is guarded with `?.`, so
        // this path never touches a nonexistent envelope.
        var envelope = await _db.BudgetCategories
            .FirstOrDefaultAsync(cat => cat.PaymentForAccountId == acc.Id, ct);

        // menunest-210: validate the close guard BEFORE any mutation — a card
        // (or loan) still owing money is not closed in real life either, and
        // menunest-205 forbids deleting its Payment envelope for the same
        // reason (closing the account would reach the same end by the side
        // door). Checked first so a refusal here can never leave a
        // half-applied rename or SortOrder change behind it — the same
        // ordering fix Task 7 made in UpdatePaymentHandler. The refusal text
        // follows menunest-212's vocabulary: จ่ายบัตร for a card, จ่ายค่างวด
        // for a loan — a Loan owner must not be told they haven't paid a
        // "card".
        if (c.IsClosed && !acc.IsClosed && PaymentEnvelopeMath.IsDebtType(acc.Type))
        {
            var balance = await _db.BudgetTransactions
                .Where(t => t.AccountId == acc.Id)
                .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
            if (balance != 0m)
            {
                var message = acc.Type == BudgetAccountType.Loan
                    ? "ยังจ่ายค่างวดไม่ครบ — ปิดบัญชีไม่ได้"
                    : "ยังจ่ายบัตรไม่ครบ — ปิดบัญชีไม่ได้";
                throw new DomainException(message);
            }
        }

        acc.Rename(c.Name);
        acc.SetSortOrder(c.SortOrder);
        // menunest-212: the envelope's name follows its Account, always — the
        // user may not rename it directly.
        envelope?.RenameForAccount(c.Name);

        if (c.IsClosed && !acc.IsClosed)
        {
            acc.Close();
            envelope?.SetHiddenForAccountClosure(true);
        }
        if (!c.IsClosed && acc.IsClosed)
        {
            acc.Reopen();
            envelope?.SetHiddenForAccountClosure(false);
        }

        await _db.SaveChangesAsync(ct);
        return new BudgetAccountDto(acc.Id, acc.Name, acc.Type, acc.Balance, acc.SortOrder, acc.IsClosed);
    }
}
