namespace MenuNest.Domain.Enums;

public enum BudgetAccountType
{
    Cash = 1,       // checking, savings, wallet — on-budget
    Credit = 2,     // credit card — on-budget but tracked separately
    Loan = 3,       // installment/loan — tracked liability
    Closed = 99     // archived, hidden from defaults
}
