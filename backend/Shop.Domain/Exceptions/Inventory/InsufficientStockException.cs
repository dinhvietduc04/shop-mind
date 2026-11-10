namespace Shop.Domain.Exceptions;

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
