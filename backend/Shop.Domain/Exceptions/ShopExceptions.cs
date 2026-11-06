using Shop.Domain.Enums;

namespace Shop.Domain.Exceptions;

public class ShopDomainException : Exception
{
    public string Code { get; }
    protected ShopDomainException(string code, string message) : base(message) => Code = code;
}

public class DomainValidationException : ShopDomainException
{
    public DomainValidationException(string message) : base("ValidationFailed", message) { }
}

public class NotFoundException : ShopDomainException
{
    public NotFoundException(string code, string message) : base(code, message) { }
}

public class ProductNotFoundException : NotFoundException
{
    public ProductNotFoundException(Guid? id = null)
        : base("ProductNotFound", id is null ? "Product not found." : $"Product '{id}' not found.") { }
}

public class CategoryNotFoundException : NotFoundException
{
    public CategoryNotFoundException(Guid? id = null)
        : base("CategoryNotFound", id is null ? "Category not found." : $"Category '{id}' not found.") { }
}

public class CartItemNotFoundException : NotFoundException
{
    public CartItemNotFoundException(Guid? id = null)
        : base("CartItemNotFound", id is null ? "Cart item not found." : $"Cart item '{id}' not found.") { }
}

public class OrderNotFoundException : NotFoundException
{
    public OrderNotFoundException(Guid? id = null)
        : base("OrderNotFound", id is null ? "Order not found." : $"Order '{id}' not found.") { }
}

public class InsufficientStockException : ShopDomainException
{
    public Guid ProductId { get; }
    public int Available { get; }
    public int Requested { get; }
    public InsufficientStockException(Guid productId, int available, int requested)
        : base("InsufficientStock", $"Insufficient stock for product '{productId}'. Available: {available}, requested: {requested}.")
    {
        ProductId = productId;
        Available = available;
        Requested = requested;
    }
}

public class InvalidOrderStateException : ShopDomainException
{
    public Guid OrderId { get; }
    public OrderStatus Current { get; }
    public OrderStatus Attempted { get; }
    public InvalidOrderStateException(Guid orderId, OrderStatus current, OrderStatus attempted)
        : base("InvalidOrderState", $"Invalid order state transition from '{current}' to '{attempted}' for order '{orderId}'.")
    {
        OrderId = orderId;
        Current = current;
        Attempted = attempted;
    }
}

public class PaymentFailedException : ShopDomainException
{
    public Guid OrderId { get; }
    public PaymentFailedException(Guid orderId, string? reason = null)
        : base("PaymentFailed", string.IsNullOrWhiteSpace(reason) ? $"Payment failed for order '{orderId}'." : reason)
    {
        OrderId = orderId;
    }
}

public class UnauthorizedOrderAccessException : ShopDomainException
{
    public UnauthorizedOrderAccessException()
        : base("UnauthorizedOrderAccess", "You do not have access to this order.") { }
}

public class EmailAlreadyExistsException : ShopDomainException
{
    public EmailAlreadyExistsException(string email)
        : base("EmailAlreadyExists", $"Email '{email}' is already registered.") { }
}

public class InvalidCredentialsException : ShopDomainException
{
    public InvalidCredentialsException()
        : base("InvalidCredentials", "Invalid email or password.") { }
}

public class InactiveProductException : ShopDomainException
{
    public InactiveProductException(Guid productId)
        : base("InactiveProduct", $"Product '{productId}' is not available for purchase.") { }
}

public class EmptyCartException : ShopDomainException
{
    public EmptyCartException()
        : base("EmptyCart", "Cart is empty.") { }
}
