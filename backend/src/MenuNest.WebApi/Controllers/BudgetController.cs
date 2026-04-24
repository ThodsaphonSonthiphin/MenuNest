using Mediator;
using MenuNest.Application.UseCases.Budget;
using MenuNest.Application.UseCases.Budget.Accounts.CreateAccount;
using MenuNest.Application.UseCases.Budget.Accounts.DeleteAccount;
using MenuNest.Application.UseCases.Budget.Accounts.ListAccounts;
using MenuNest.Application.UseCases.Budget.Accounts.UpdateAccount;
using MenuNest.Application.UseCases.Budget.Groups.CreateGroup;
using MenuNest.Application.UseCases.Budget.Groups.DeleteGroup;
using MenuNest.Application.UseCases.Budget.Groups.ListGroups;
using MenuNest.Application.UseCases.Budget.Groups.UpdateGroup;
using MenuNest.Application.UseCases.Budget.Categories.CreateCategory;
using MenuNest.Application.UseCases.Budget.Categories.DeleteCategory;
using MenuNest.Application.UseCases.Budget.Categories.UpdateCategory;
using MenuNest.Application.UseCases.Budget.Monthly.CoverOverspending;
using MenuNest.Application.UseCases.Budget.Monthly.GetMonthlySummary;
using MenuNest.Application.UseCases.Budget.Monthly.MoveMoney;
using MenuNest.Application.UseCases.Budget.Monthly.SetAssignedAmount;
using MenuNest.Application.UseCases.Budget.Monthly.SetMonthlyIncome;
using MenuNest.Application.UseCases.Budget.Transactions.CreateTransaction;
using MenuNest.Application.UseCases.Budget.Transactions.DeleteTransaction;
using MenuNest.Application.UseCases.Budget.Transactions.ListTransactions;
using MenuNest.Application.UseCases.Budget.Transactions.UpdateTransaction;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/budget")]
public sealed class BudgetController : ControllerBase
{
    private readonly IMediator _m;
    public BudgetController(IMediator m) { _m = m; }

    // ----- summary (page load) -----
    [HttpGet("summary")]
    public async Task<ActionResult<MonthlySummaryDto>> Summary(
        [FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Ok(await _m.Send(new GetMonthlySummaryQuery(year, month), ct));

    // ----- accounts -----
    [HttpGet("accounts")]
    public async Task<ActionResult<IReadOnlyList<BudgetAccountDto>>> ListAccounts(CancellationToken ct) =>
        Ok(await _m.Send(new ListAccountsQuery(), ct));

    [HttpPost("accounts")]
    public async Task<ActionResult<BudgetAccountDto>> CreateAccount(
        [FromBody] CreateAccountRequest r, CancellationToken ct) =>
        Ok(await _m.Send(new CreateAccountCommand(r.Name, r.Type, r.OpeningBalance, r.SortOrder), ct));

    [HttpPut("accounts/{id:guid}")]
    public async Task<ActionResult<BudgetAccountDto>> UpdateAccount(
        Guid id, [FromBody] UpdateAccountRequest r, CancellationToken ct) =>
        Ok(await _m.Send(new UpdateAccountCommand(id, r.Name, r.SortOrder, r.IsClosed, r.SetBalance), ct));

    [HttpDelete("accounts/{id:guid}")]
    public async Task<IActionResult> DeleteAccount(Guid id, CancellationToken ct)
    { await _m.Send(new DeleteAccountCommand(id), ct); return NoContent(); }

    // ----- groups -----
    [HttpGet("groups")]
    public async Task<ActionResult<IReadOnlyList<CategoryGroupDto>>> ListGroups(CancellationToken ct) =>
        Ok(await _m.Send(new ListGroupsQuery(), ct));

    [HttpPost("groups")]
    public async Task<ActionResult<CategoryGroupDto>> CreateGroup(
        [FromBody] UpsertGroupRequest r, CancellationToken ct) =>
        Ok(await _m.Send(new CreateGroupCommand(r.Name, r.SortOrder), ct));

    [HttpPut("groups/{id:guid}")]
    public async Task<ActionResult<CategoryGroupDto>> UpdateGroup(
        Guid id, [FromBody] UpsertGroupRequest r, CancellationToken ct) =>
        Ok(await _m.Send(new UpdateGroupCommand(id, r.Name, r.SortOrder), ct));

    [HttpDelete("groups/{id:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid id, CancellationToken ct)
    { await _m.Send(new DeleteGroupCommand(id), ct); return NoContent(); }

    // ----- categories -----
    [HttpPost("categories")]
    public async Task<ActionResult<BudgetCategoryDto>> CreateCategory(
        [FromBody] UpsertCategoryRequest r, CancellationToken ct) =>
        Ok(await _m.Send(new CreateCategoryCommand(
            r.GroupId, r.Name, r.Emoji, r.SortOrder,
            r.TargetType, r.TargetAmount, r.TargetDueDate, r.TargetDayOfMonth), ct));

    [HttpPut("categories/{id:guid}")]
    public async Task<ActionResult<BudgetCategoryDto>> UpdateCategory(
        Guid id, [FromBody] UpsertCategoryRequest r, CancellationToken ct) =>
        Ok(await _m.Send(new UpdateCategoryCommand(id,
            r.GroupId, r.Name, r.Emoji, r.SortOrder,
            r.TargetType, r.TargetAmount, r.TargetDueDate, r.TargetDayOfMonth), ct));

    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct)
    { await _m.Send(new DeleteCategoryCommand(id), ct); return NoContent(); }

    // ----- monthly ops -----
    [HttpPut("monthly/income")]
    public async Task<IActionResult> SetIncome([FromBody] SetMonthlyIncomeRequest r, CancellationToken ct)
    { await _m.Send(new SetMonthlyIncomeCommand(r.Year, r.Month, r.Amount), ct); return NoContent(); }

    [HttpPut("monthly/assigned")]
    public async Task<IActionResult> SetAssigned([FromBody] SetAssignedRequest r, CancellationToken ct)
    { await _m.Send(new SetAssignedAmountCommand(r.CategoryId, r.Year, r.Month, r.Amount), ct); return NoContent(); }

    [HttpPost("monthly/move")]
    public async Task<IActionResult> Move([FromBody] MoveMoneyRequest r, CancellationToken ct)
    { await _m.Send(new MoveMoneyCommand(r.FromCategoryId, r.ToCategoryId, r.Year, r.Month, r.Amount), ct); return NoContent(); }

    [HttpPost("monthly/cover")]
    public async Task<IActionResult> Cover([FromBody] CoverOverspendingRequest r, CancellationToken ct)
    { await _m.Send(new CoverOverspendingCommand(r.OverspentCategoryId, r.FromCategoryId, r.Year, r.Month, r.Amount), ct); return NoContent(); }

    // ----- transactions -----
    [HttpGet("transactions")]
    public async Task<ActionResult<IReadOnlyList<BudgetTransactionDto>>> ListTx(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? categoryId, CancellationToken ct) =>
        Ok(await _m.Send(new ListTransactionsQuery(year, month, categoryId), ct));

    [HttpPost("transactions")]
    public async Task<ActionResult<BudgetTransactionDto>> CreateTx(
        [FromBody] CreateTransactionRequest r, CancellationToken ct) =>
        Ok(await _m.Send(new CreateTransactionCommand(r.AccountId, r.CategoryId, r.Amount, r.Date, r.Notes), ct));

    [HttpPut("transactions/{id:guid}")]
    public async Task<ActionResult<BudgetTransactionDto>> UpdateTx(
        Guid id, [FromBody] UpdateTransactionRequest r, CancellationToken ct) =>
        Ok(await _m.Send(new UpdateTransactionCommand(id, r.AccountId, r.CategoryId, r.Amount, r.Date, r.Notes), ct));

    [HttpDelete("transactions/{id:guid}")]
    public async Task<IActionResult> DeleteTx(Guid id, CancellationToken ct)
    { await _m.Send(new DeleteTransactionCommand(id), ct); return NoContent(); }
}
