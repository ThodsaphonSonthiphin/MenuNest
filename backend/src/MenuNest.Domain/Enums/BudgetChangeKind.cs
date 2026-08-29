namespace MenuNest.Domain.Enums;

/// <summary>
/// The four budget acts Undo covers (menunest-196). Everything else —
/// transactions, balance corrections, account / Envelope / group CRUD — is
/// deliberately out of scope and is never recorded.
/// </summary>
public enum BudgetChangeKind
{
    Assign = 0,
    Move = 1,
    Cover = 2,
    EverydayMark = 3,
}
