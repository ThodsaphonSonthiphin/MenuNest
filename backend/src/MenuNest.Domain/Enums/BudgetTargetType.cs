namespace MenuNest.Domain.Enums;

public enum BudgetTargetType
{
    None = 0,
    MonthlyAmount = 1,     // "need ฿X every month"
    ByDate = 2,            // "need ฿X by yyyy-mm-dd"
    MonthlySavingsBuilder = 3 // "save ฿X every month, never resets"
}
