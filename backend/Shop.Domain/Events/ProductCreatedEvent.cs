using Shop.Domain.Enums;

namespace Shop.Domain.Events;

/// <summary>Catalog v1: product.created { productId, sku, price, status }</summary>
public sealed record ProductCreatedEvent(Guid ProductId, string Sku, decimal Price, ProductStatus Status) : IDomainEvent
{
    public string EventType => "product.created";
    public string AggregateType => "product";
    public Guid AggregateId => ProductId;
    public Guid? UserId => null;
    public object Data => new { productId = ProductId, sku = Sku, price = Price, status = Status.ToString() };
}
