namespace Shop.Domain.Exceptions;

public class UnauthorizedOrderAccessException : ShopDomainException
{
    public UnauthorizedOrderAccessException()
        : base("UnauthorizedOrderAccess", "You do not have access to this order.") { }
}
