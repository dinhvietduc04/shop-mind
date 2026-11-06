using Shop.Application.Dtos;
using Shop.Domain.Entities;

namespace Shop.Application.Services;

public static class Mapping
{
    public static UserDto ToDto(this User u)
        => new(u.Id, u.Email, u.FirstName, u.LastName, u.PhoneNumber, u.Role);

    public static CategoryDto ToDto(this Category c)
        => new(c.Id, c.Name, c.Slug, c.Description, c.IsActive);

    public static ProductImageDto ToDto(this ProductImage i)
        => new(i.Id, i.Url, i.AltText, i.DisplayOrder);

    public static ProductDto ToDto(this Product p)
        => new(
            p.Id, p.Name, p.Slug, p.SKU, p.Brand, p.Price, p.Status,
            p.CategoryId, p.Category?.Name,
            p.Inventory is null ? 0 : p.Inventory.Quantity - p.Inventory.ReservedQuantity,
            p.Images.OrderBy(i => i.DisplayOrder).Select(ToDto).ToList());

    public static ProductDetailDto ToDetailDto(this Product p)
        => new(
            p.Id, p.Name, p.Slug, p.SKU, p.Description, p.Brand, p.Price, p.Status,
            p.CategoryId, p.Category?.Name,
            p.Inventory is null ? 0 : p.Inventory.Quantity - p.Inventory.ReservedQuantity,
            p.Images.OrderBy(i => i.DisplayOrder).Select(ToDto).ToList(),
            p.CreatedAt);

    public static CartItemDto ToDto(this CartItem i)
        => new(
            i.Id, i.ProductId,
            i.Product?.Name ?? string.Empty,
            i.Product?.SKU ?? string.Empty,
            i.Quantity, i.UnitPrice, i.UnitPrice * i.Quantity,
            i.Product?.Images.OrderBy(x => x.DisplayOrder).FirstOrDefault()?.Url);

    public static CartDto ToDto(this Cart c)
    {
        var items = c.Items.Select(ToDto).ToList();
        return new(c.Id, items, items.Sum(i => i.Subtotal), items.Sum(i => i.Quantity));
    }

    public static ShippingAddressDto ToDto(this Domain.ValueObjects.ShippingAddress a)
        => new(a.FullName, a.Phone, a.Address, a.City);

    public static Domain.ValueObjects.ShippingAddress ToVo(this ShippingAddressDto d)
        => new() { FullName = d.FullName, Phone = d.Phone, Address = d.Address, City = d.City };

    public static PaymentDto ToDto(this Payment p)
        => new(p.Id, p.Amount, p.Status.ToString(), p.TransactionId);

    public static OrderItemDto ToDto(this OrderItem i)
        => new(i.ProductId, i.ProductName, i.SKU, i.UnitPrice, i.Quantity, i.Subtotal);

    public static OrderDto ToDto(this Order o)
        => new(
            o.Id, o.OrderNumber, o.Status.ToString(),
            o.Subtotal, o.ShippingFee, o.TotalAmount,
            o.ShippingAddress.ToDto(), o.CreatedAt,
            o.Items.Select(ToDto).ToList(),
            o.Payment is null ? null : o.Payment.ToDto());

    public static InventoryDto ToDto(this Inventory inv)
        => new(inv.ProductId, inv.Product?.Name ?? string.Empty, inv.Product?.SKU ?? string.Empty,
            inv.Quantity, inv.ReservedQuantity, inv.Quantity - inv.ReservedQuantity);
}
