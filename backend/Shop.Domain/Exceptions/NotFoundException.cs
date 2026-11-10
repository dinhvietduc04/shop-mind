namespace Shop.Domain.Exceptions;

public class NotFoundException : ShopDomainException
{
    public NotFoundException(string code, string message) : base(code, message) { }
}
