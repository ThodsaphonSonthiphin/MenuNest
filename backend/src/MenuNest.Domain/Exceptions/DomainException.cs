namespace MenuNest.Domain.Exceptions;

/// <summary>
/// Raised when a domain invariant is violated. The Application layer
/// translates these into 400-class responses.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception inner) : base(message, inner) { }
}
