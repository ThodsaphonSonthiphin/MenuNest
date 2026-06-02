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

namespace MenuNest.McpServer.Tools;

[McpServerToolType]
public sealed class BudgetTools(IMediator mediator)
{
    // ── Summary ──────────────────────────────────────────────────────────────

    [McpServerTool, Description("Get the monthly budget summary including income, assigned amounts, available to assign, per-category spent and available balances, and all envelope groups with their categories")]
    public async Task<MonthlySummaryDto> get_budget_summary(
        [Description("Year (e.g. 2026)")] int year,
        [Description("Month 1–12")] int month,
        CancellationToken ct)
        => await mediator.Send(new GetMonthlySummaryQuery(year, month), ct);

    // ── Accounts ─────────────────────────────────────────────────────────────

    [McpServerTool, Description("List all budget accounts (checking, savings, credit cards, loans)")]
    public async Task<IReadOnlyList<BudgetAccountDto>> list_budget_accounts(
        CancellationToken ct)
        => await mediator.Send(new ListAccountsQuery(), ct);

    [McpServerTool, Description("Create a new budget account")]
    public async Task<BudgetAccountDto> create_budget_account(
        [Description("Account name")] string name,
        [Description("Account type: Cash, Credit, Loan, or Closed")] BudgetAccountType type,
        [Description("Opening balance (use negative for liabilities such as credit cards)")] decimal openingBalance,
        CancellationToken ct)
        => await mediator.Send(new CreateAccountCommand(name, type, openingBalance), ct);

    [McpServerTool, Description("Update a budget account's name, sort order, closed status, or manually set its balance")]
    public async Task<BudgetAccountDto> update_budget_account(
        [Description("Account ID")] Guid id,
        [Description("Account name")] string name,
        [Description("Display sort order")] int sortOrder,
        [Description("Whether the account is closed/archived")] bool isClosed,
        [Description("Optional: override the account balance to this exact value (null to skip)")] decimal? setBalance,
        CancellationToken ct)
        => await mediator.Send(new UpdateAccountCommand(id, name, sortOrder, isClosed, setBalance), ct);

    [McpServerTool, Description("Delete a budget account by ID")]
    public async Task delete_budget_account(
        [Description("Account ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteAccountCommand(id), ct);

    [McpServerTool, Description("List transactions for a specific account, with month summary and pagination")]
    public async Task<AccountTransactionsPageDto> list_account_transactions(
        [Description("Account ID")] Guid accountId,
        [Description("Year for inflow/outflow summary")] int year,
        [Description("Month for inflow/outflow summary")] int month,
        [Description("Number of transactions to skip (for pagination)")] int skip,
        [Description("Maximum number of transactions to return (e.g. 20)")] int take,
        CancellationToken ct)
        => await mediator.Send(new ListAccountTransactionsQuery(accountId, year, month, skip, take), ct);

    // ── Groups ───────────────────────────────────────────────────────────────

    [McpServerTool, Description("List all budget category groups")]
    public async Task<IReadOnlyList<CategoryGroupDto>> list_budget_groups(
        CancellationToken ct)
        => await mediator.Send(new ListGroupsQuery(), ct);

    [McpServerTool, Description("Create a new budget category group")]
    public async Task<CategoryGroupDto> create_budget_group(
        [Description("Group name")] string name,
        CancellationToken ct)
        => await mediator.Send(new CreateGroupCommand(name), ct);

    [McpServerTool, Description("Update a budget category group's name or sort order")]
    public async Task<CategoryGroupDto> update_budget_group(
        [Description("Group ID")] Guid id,
        [Description("Group name")] string name,
        [Description("Display sort order")] int sortOrder,
        CancellationToken ct)
        => await mediator.Send(new UpdateGroupCommand(id, name, sortOrder), ct);

    [McpServerTool, Description("Delete a budget category group by ID")]
    public async Task delete_budget_group(
        [Description("Group ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteGroupCommand(id), ct);

    // ── Categories ───────────────────────────────────────────────────────────

    [McpServerTool, Description("Create a new budget category within a group, with an optional savings target")]
    public async Task<BudgetCategoryDto> create_budget_category(
        [Description("Parent group ID")] Guid groupId,
        [Description("Category name")] string name,
        [Description("Optional emoji icon")] string? emoji,
        [Description("Target type: None, MonthlyAmount, ByDate, or MonthlySavingsBuilder")] BudgetTargetType targetType,
        [Description("Target amount (required for MonthlyAmount, ByDate, and MonthlySavingsBuilder targets)")] decimal? targetAmount,
        [Description("Target due date (required for ByDate target)")] DateOnly? targetDueDate,
        [Description("Day of month for recurring targets (used with MonthlySavingsBuilder)")] int? targetDayOfMonth,
        CancellationToken ct)
        => await mediator.Send(new CreateCategoryCommand(groupId, name, emoji, targetType, targetAmount, targetDueDate, targetDayOfMonth), ct);

    [McpServerTool, Description("Update a budget category's name, group, emoji, sort order, or target settings")]
    public async Task<BudgetCategoryDto> update_budget_category(
        [Description("Category ID")] Guid id,
        [Description("Parent group ID")] Guid groupId,
        [Description("Category name")] string name,
        [Description("Optional emoji icon")] string? emoji,
        [Description("Display sort order")] int sortOrder,
        [Description("Target type: None, MonthlyAmount, ByDate, or MonthlySavingsBuilder")] BudgetTargetType targetType,
        [Description("Target amount (null for no target)")] decimal? targetAmount,
        [Description("Target due date (null for no date target)")] DateOnly? targetDueDate,
        [Description("Day of month for recurring targets")] int? targetDayOfMonth,
        CancellationToken ct)
        => await mediator.Send(new UpdateCategoryCommand(id, groupId, name, emoji, sortOrder, targetType, targetAmount, targetDueDate, targetDayOfMonth), ct);

    [McpServerTool, Description("Delete a budget category by ID")]
    public async Task delete_budget_category(
        [Description("Category ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteCategoryCommand(id), ct);

    // ── Assigned amounts ──────────────────────────────────────────────────────

    [McpServerTool, Description("Set the assigned (budgeted) amount for a category in a given month")]
    public async Task set_assigned_amount(
        [Description("Category ID")] Guid categoryId,
        [Description("Year")] int year,
        [Description("Month 1–12")] int month,
        [Description("Amount to assign (replaces any existing assigned amount)")] decimal amount,
        CancellationToken ct)
        => await mediator.Send(new SetAssignedAmountCommand(categoryId, year, month, amount), ct);

    [McpServerTool, Description("Move money from one category envelope to another within the same month")]
    public async Task move_money(
        [Description("Source category ID")] Guid fromCategoryId,
        [Description("Destination category ID")] Guid toCategoryId,
        [Description("Year")] int year,
        [Description("Month 1–12")] int month,
        [Description("Amount to move")] decimal amount,
        CancellationToken ct)
        => await mediator.Send(new MoveMoneyCommand(fromCategoryId, toCategoryId, year, month, amount), ct);

    [McpServerTool, Description("Cover overspending in a category by pulling funds from another category")]
    public async Task cover_overspending(
        [Description("Overspent category ID that needs to be covered")] Guid overspentCategoryId,
        [Description("Source category ID to take funds from")] Guid fromCategoryId,
        [Description("Year")] int year,
        [Description("Month 1–12")] int month,
        [Description("Amount to cover")] decimal amount,
        CancellationToken ct)
        => await mediator.Send(new CoverOverspendingCommand(overspentCategoryId, fromCategoryId, year, month, amount), ct);

    // ── Transactions ──────────────────────────────────────────────────────────

    [McpServerTool, Description("List transactions for a given month, optionally filtered by category")]
    public async Task<IReadOnlyList<BudgetTransactionDto>> list_transactions(
        [Description("Year")] int year,
        [Description("Month 1–12")] int month,
        [Description("Optional category ID filter")] Guid? categoryId,
        CancellationToken ct)
        => await mediator.Send(new ListTransactionsQuery(year, month, categoryId), ct);

    [McpServerTool, Description("Create a new budget transaction")]
    public async Task<BudgetTransactionDto> create_transaction(
        [Description("Account ID")] Guid accountId,
        [Description("Optional category ID (null for uncategorised income/transfers)")] Guid? categoryId,
        [Description("Amount (positive for income, negative for expenses)")] decimal amount,
        [Description("Transaction date")] DateOnly date,
        [Description("Optional notes")] string? notes,
        CancellationToken ct)
        => await mediator.Send(new CreateTransactionCommand(accountId, categoryId, amount, date, notes), ct);

    [McpServerTool, Description("Update an existing budget transaction")]
    public async Task<BudgetTransactionDto> update_transaction(
        [Description("Transaction ID")] Guid id,
        [Description("Account ID")] Guid accountId,
        [Description("Optional category ID")] Guid? categoryId,
        [Description("Amount (positive for income, negative for expenses)")] decimal amount,
        [Description("Transaction date")] DateOnly date,
        [Description("Optional notes")] string? notes,
        CancellationToken ct)
        => await mediator.Send(new UpdateTransactionCommand(id, accountId, categoryId, amount, date, notes), ct);

    [McpServerTool, Description("Delete a budget transaction by ID")]
    public async Task delete_transaction(
        [Description("Transaction ID")] Guid id,
        CancellationToken ct)
        => await mediator.Send(new DeleteTransactionCommand(id), ct);
}
