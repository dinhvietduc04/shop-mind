namespace Shop.Domain.Exceptions;

public class OrderNotFoundException : NotFoundException
{
    public OrderNotFoundException(Guid? id = null)
        : base("OrderNotFound", id is null ? "Order not found." : $"Order '{id}' not found.") { }
}
