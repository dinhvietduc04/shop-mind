namespace Shop.Domain.Exceptions;

public class DomainValidationException : ShopDomainException
{
    public DomainValidationException(string message) : base("ValidationFailed", message) { }
}
