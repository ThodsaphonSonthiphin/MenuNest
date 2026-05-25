using Mediator;

namespace MenuNest.Application.UseCases.Budget.Accounts.ListAccountTransactions;

/// <summary>
/// Read the account-detail page: account summary (balance + month
/// inflow/outflow) plus a page of that account's transactions ordered
/// by CreatedAt DESC. Year/Month frame the in/out summary; Skip/Take
/// paginate the transaction list.
/// </summary>
public sealed record ListAccountTransactionsQuery(
    Guid AccountId,
    int Year,
    int Month,
    int Skip,
    int Take
) : IQuery<AccountTransactionsPageDto>;
