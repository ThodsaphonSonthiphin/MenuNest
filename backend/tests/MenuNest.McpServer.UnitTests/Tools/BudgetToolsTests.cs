using Mediator;
using MenuNest.Application.UseCases.Budget;
using MenuNest.Application.UseCases.Budget.Accounts.ListAccounts;
using MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;
using MenuNest.Application.UseCases.Budget.Accounts.UpdateAccount;
using MenuNest.Application.UseCases.Budget.Accounts.DeleteAccount;
using MenuNest.Application.UseCases.Budget.Accounts.ListAccountTransactions;
using MenuNest.Application.UseCases.Budget.Groups.ListGroups;
using MenuNest.Application.UseCases.Budget.Groups.CreateGroup;
using MenuNest.Application.UseCases.Budget.Groups.UpdateGroup;
using MenuNest.Application.UseCases.Budget.Groups.DeleteGroup;
using MenuNest.Application.UseCases.Budget.Categories.CreateCategory;
using MenuNest.Application.UseCases.Budget.Categories.UpdateCategory;
using MenuNest.Application.UseCases.Budget.Categories.DeleteCategory;
using MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;
using MenuNest.Application.UseCases.Budget.Monthly.SetAssignedAmount;
using MenuNest.Application.UseCases.Budget.Monthly.MoveMoney;
using MenuNest.Application.UseCases.Budget.Monthly.CoverOverspending;
using MenuNest.Application.UseCases.Budget.Transactions.ListTransactions;
using MenuNest.Application.UseCases.Budget.Transactions.CreateTransaction;
using MenuNest.Application.UseCases.Budget.Transactions.UpdateTransaction;
using MenuNest.Application.UseCases.Budget.Transactions.DeleteTransaction;
using MenuNest.Domain.Enums;
using MenuNest.McpServer.Tools;
using Moq;

namespace MenuNest.McpServer.UnitTests.Tools;

public class BudgetToolsTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly BudgetTools _sut;

    public BudgetToolsTests() => _sut = new BudgetTools(_mediator.Object);

    // ── Summary ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task get_budget_summary_sends_GetMonthlySummaryQuery_with_year_and_month()
    {
        _mediator
            .Setup(m => m.Send(It.Is<GetMonthlySummaryQuery>(q => q.Year == 2026 && q.Month == 6), It.IsAny<CancellationToken>()))
            .Returns<GetMonthlySummaryQuery, CancellationToken>((_, _) => new ValueTask<MonthlySummaryDto>((MonthlySummaryDto)default!));
        await _sut.get_budget_summary(2026, 6, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<GetMonthlySummaryQuery>(q => q.Year == 2026 && q.Month == 6), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Accounts ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task list_budget_accounts_sends_ListAccountsQuery()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListAccountsQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<BudgetAccountDto>>(new List<BudgetAccountDto>()));
        await _sut.list_budget_accounts(CancellationToken.None);
        _mediator.Verify(m => m.Send(It.IsAny<ListAccountsQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task create_budget_account_sends_CreateAccountCommand_with_correct_fields()
    {
        _mediator
            .Setup(m => m.Send(It.Is<CreateAccountCommand>(c => c.Name == "Wallet" && c.Type == BudgetAccountType.Cash && c.OpeningBalance == 500m), It.IsAny<CancellationToken>()))
            .Returns<CreateAccountCommand, CancellationToken>((_, _) => new ValueTask<BudgetAccountDto>((BudgetAccountDto)default!));
        await _sut.create_budget_account("Wallet", BudgetAccountType.Cash, 500m, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<CreateAccountCommand>(c => c.Name == "Wallet" && c.Type == BudgetAccountType.Cash && c.OpeningBalance == 500m), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task update_budget_account_sends_UpdateAccountCommand_with_correct_fields()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<UpdateAccountCommand>(c => c.Id == id && c.Name == "Savings" && c.SortOrder == 2 && c.IsClosed == false && c.SetBalance == null), It.IsAny<CancellationToken>()))
            .Returns<UpdateAccountCommand, CancellationToken>((_, _) => new ValueTask<BudgetAccountDto>((BudgetAccountDto)default!));
        await _sut.update_budget_account(id, "Savings", 2, false, null, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<UpdateAccountCommand>(c => c.Id == id && c.Name == "Savings" && c.SortOrder == 2 && c.IsClosed == false && c.SetBalance == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_budget_account_sends_DeleteAccountCommand_with_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeleteAccountCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        await _sut.delete_budget_account(id, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<DeleteAccountCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task list_account_transactions_sends_ListAccountTransactionsQuery_with_all_params()
    {
        var accountId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<ListAccountTransactionsQuery>(q => q.AccountId == accountId && q.Year == 2026 && q.Month == 6 && q.Skip == 0 && q.Take == 20), It.IsAny<CancellationToken>()))
            .Returns<ListAccountTransactionsQuery, CancellationToken>((_, _) => new ValueTask<AccountTransactionsPageDto>((AccountTransactionsPageDto)default!));
        await _sut.list_account_transactions(accountId, 2026, 6, 0, 20, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<ListAccountTransactionsQuery>(q => q.AccountId == accountId && q.Year == 2026 && q.Month == 6 && q.Skip == 0 && q.Take == 20), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Groups ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task list_budget_groups_sends_ListGroupsQuery()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListGroupsQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<CategoryGroupDto>>(new List<CategoryGroupDto>()));
        await _sut.list_budget_groups(CancellationToken.None);
        _mediator.Verify(m => m.Send(It.IsAny<ListGroupsQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task create_budget_group_sends_CreateGroupCommand_with_name()
    {
        _mediator
            .Setup(m => m.Send(It.Is<CreateGroupCommand>(c => c.Name == "Housing"), It.IsAny<CancellationToken>()))
            .Returns<CreateGroupCommand, CancellationToken>((_, _) => new ValueTask<CategoryGroupDto>((CategoryGroupDto)default!));
        await _sut.create_budget_group("Housing", CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<CreateGroupCommand>(c => c.Name == "Housing"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task update_budget_group_sends_UpdateGroupCommand_with_correct_fields()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<UpdateGroupCommand>(c => c.Id == id && c.Name == "Transport" && c.SortOrder == 3), It.IsAny<CancellationToken>()))
            .Returns<UpdateGroupCommand, CancellationToken>((_, _) => new ValueTask<CategoryGroupDto>((CategoryGroupDto)default!));
        await _sut.update_budget_group(id, "Transport", 3, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<UpdateGroupCommand>(c => c.Id == id && c.Name == "Transport" && c.SortOrder == 3), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_budget_group_sends_DeleteGroupCommand_with_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeleteGroupCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        await _sut.delete_budget_group(id, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<DeleteGroupCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Categories ────────────────────────────────────────────────────────────

    [Fact]
    public async Task create_budget_category_sends_CreateCategoryCommand_with_correct_fields()
    {
        var groupId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<CreateCategoryCommand>(c =>
                c.GroupId == groupId &&
                c.Name == "Groceries" &&
                c.Emoji == "🛒" &&
                c.TargetType == BudgetTargetType.MonthlyAmount &&
                c.TargetAmount == 5000m &&
                c.TargetDueDate == null &&
                c.TargetDayOfMonth == null), It.IsAny<CancellationToken>()))
            .Returns<CreateCategoryCommand, CancellationToken>((_, _) => new ValueTask<BudgetCategoryDto>((BudgetCategoryDto)default!));
        await _sut.create_budget_category(groupId, "Groceries", "🛒", BudgetTargetType.MonthlyAmount, 5000m, null, null, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<CreateCategoryCommand>(c =>
            c.GroupId == groupId &&
            c.Name == "Groceries" &&
            c.TargetType == BudgetTargetType.MonthlyAmount &&
            c.TargetAmount == 5000m), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task update_budget_category_sends_UpdateCategoryCommand_with_correct_fields()
    {
        var id = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<UpdateCategoryCommand>(c =>
                c.Id == id &&
                c.GroupId == groupId &&
                c.Name == "Utilities" &&
                c.SortOrder == 1 &&
                c.TargetType == BudgetTargetType.None), It.IsAny<CancellationToken>()))
            .Returns<UpdateCategoryCommand, CancellationToken>((_, _) => new ValueTask<BudgetCategoryDto>((BudgetCategoryDto)default!));
        await _sut.update_budget_category(id, groupId, "Utilities", null, 1, BudgetTargetType.None, null, null, null, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<UpdateCategoryCommand>(c =>
            c.Id == id &&
            c.GroupId == groupId &&
            c.Name == "Utilities" &&
            c.SortOrder == 1 &&
            c.TargetType == BudgetTargetType.None), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_budget_category_sends_DeleteCategoryCommand_with_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeleteCategoryCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        await _sut.delete_budget_category(id, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<DeleteCategoryCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Assigned amounts ──────────────────────────────────────────────────────

    [Fact]
    public async Task set_assigned_amount_sends_SetAssignedAmountCommand_with_correct_fields()
    {
        var categoryId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<SetAssignedAmountCommand>(c => c.CategoryId == categoryId && c.Year == 2026 && c.Month == 6 && c.Amount == 3000m), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        await _sut.set_assigned_amount(categoryId, 2026, 6, 3000m, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<SetAssignedAmountCommand>(c => c.CategoryId == categoryId && c.Year == 2026 && c.Month == 6 && c.Amount == 3000m), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task move_money_sends_MoveMoneyCommand_with_correct_categories()
    {
        var fromId = Guid.NewGuid();
        var toId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<MoveMoneyCommand>(c => c.FromCategoryId == fromId && c.ToCategoryId == toId && c.Year == 2026 && c.Month == 6 && c.Amount == 100m), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        await _sut.move_money(fromId, toId, 2026, 6, 100m, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<MoveMoneyCommand>(c => c.FromCategoryId == fromId && c.ToCategoryId == toId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task cover_overspending_sends_CoverOverspendingCommand_with_correct_categories()
    {
        var overspentId = Guid.NewGuid();
        var fromId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<CoverOverspendingCommand>(c => c.OverspentCategoryId == overspentId && c.FromCategoryId == fromId && c.Year == 2026 && c.Month == 6 && c.Amount == 200m), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        await _sut.cover_overspending(overspentId, fromId, 2026, 6, 200m, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<CoverOverspendingCommand>(c => c.OverspentCategoryId == overspentId && c.FromCategoryId == fromId && c.Amount == 200m), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Transactions ──────────────────────────────────────────────────────────

    [Fact]
    public async Task list_transactions_sends_ListTransactionsQuery_with_year_month_and_no_category_filter()
    {
        _mediator
            .Setup(m => m.Send(It.Is<ListTransactionsQuery>(q => q.Year == 2026 && q.Month == 6 && q.CategoryId == null), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<BudgetTransactionDto>>(new List<BudgetTransactionDto>()));
        await _sut.list_transactions(2026, 6, null, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<ListTransactionsQuery>(q => q.Year == 2026 && q.Month == 6 && q.CategoryId == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task list_transactions_passes_category_filter_to_query()
    {
        var categoryId = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<ListTransactionsQuery>(q => q.CategoryId == categoryId), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyList<BudgetTransactionDto>>(new List<BudgetTransactionDto>()));
        await _sut.list_transactions(2026, 6, categoryId, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<ListTransactionsQuery>(q => q.CategoryId == categoryId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task create_transaction_sends_CreateTransactionCommand_with_correct_fields()
    {
        var accountId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 1);
        _mediator
            .Setup(m => m.Send(It.Is<CreateTransactionCommand>(c => c.AccountId == accountId && c.CategoryId == categoryId && c.Amount == 99.99m && c.Date == date && c.Notes == null), It.IsAny<CancellationToken>()))
            .Returns<CreateTransactionCommand, CancellationToken>((_, _) => new ValueTask<BudgetTransactionDto>((BudgetTransactionDto)default!));
        await _sut.create_transaction(accountId, categoryId, 99.99m, date, null, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<CreateTransactionCommand>(c => c.AccountId == accountId && c.Amount == 99.99m), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task create_transaction_allows_null_category()
    {
        var accountId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 15);
        _mediator
            .Setup(m => m.Send(It.Is<CreateTransactionCommand>(c => c.AccountId == accountId && c.CategoryId == null), It.IsAny<CancellationToken>()))
            .Returns<CreateTransactionCommand, CancellationToken>((_, _) => new ValueTask<BudgetTransactionDto>((BudgetTransactionDto)default!));
        await _sut.create_transaction(accountId, null, 1000m, date, "Salary", CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<CreateTransactionCommand>(c => c.CategoryId == null && c.Notes == "Salary"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task update_transaction_sends_UpdateTransactionCommand_with_correct_fields()
    {
        var id = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 10);
        _mediator
            .Setup(m => m.Send(It.Is<UpdateTransactionCommand>(c => c.Id == id && c.AccountId == accountId && c.CategoryId == categoryId && c.Amount == -250m && c.Date == date && c.Notes == "Electricity"), It.IsAny<CancellationToken>()))
            .Returns<UpdateTransactionCommand, CancellationToken>((_, _) => new ValueTask<BudgetTransactionDto>((BudgetTransactionDto)default!));
        await _sut.update_transaction(id, accountId, categoryId, -250m, date, "Electricity", CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<UpdateTransactionCommand>(c => c.Id == id && c.Amount == -250m && c.Notes == "Electricity"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task delete_transaction_sends_DeleteTransactionCommand_with_id()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.Is<DeleteTransactionCommand>(c => c.Id == id), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Unit>(Unit.Value));
        await _sut.delete_transaction(id, CancellationToken.None);
        _mediator.Verify(m => m.Send(It.Is<DeleteTransactionCommand>(c => c.Id == id), It.IsAny<CancellationToken>()), Times.Once);
    }
}
