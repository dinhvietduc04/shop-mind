namespace Shop.Domain.Exceptions;

public class EmptyCartException : ShopDomainException
{
    public EmptyCartException()
        : base("EmptyCart", "Cart is empty.") { }
}
