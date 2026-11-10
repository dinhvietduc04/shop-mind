namespace Shop.Domain.Exceptions;

public class CartItemNotFoundException : NotFoundException
{
    public CartItemNotFoundException(Guid? id = null)
        : base("CartItemNotFound", id is null ? "Cart item not found." : $"Cart item '{id}' not found.") { }
}
