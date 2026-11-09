namespace Shop.Domain.Exceptions;

public class ProductNotFoundException : NotFoundException
{
    public ProductNotFoundException(Guid? id = null)
        : base("ProductNotFound", id is null ? "Product not found." : $"Product '{id}' not found.") { }
}
