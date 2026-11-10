namespace Shop.Domain.Exceptions;

/// <summary>A3: a malformed or unregistered spec-filter clause (fail-closed 400).</summary>
public class InvalidSpecFilterException : ShopDomainException
{
    public InvalidSpecFilterException(string message) : base("InvalidSpecFilter", message) { }
}
