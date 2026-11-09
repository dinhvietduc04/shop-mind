namespace Shop.Domain.Exceptions;

public class InactiveProductException : ShopDomainException
{
    public InactiveProductException(Guid productId)
        : base("InactiveProduct", $"Product '{productId}' is not available for purchase.") { }
}
