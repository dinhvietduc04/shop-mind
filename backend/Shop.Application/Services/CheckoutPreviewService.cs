using Microsoft.EntityFrameworkCore;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Application.Pricing;
using Shop.Domain.Exceptions;

namespace Shop.Application.Services;

/// <summary>
/// M13: a dry-run of the quote path. Same cart, same server prices, same
/// shipping rule — but read-only. Stock is inspected, never reserved, so the
/// preview can never hold inventory or race the human's own quote.
/// </summary>
public class CheckoutPreviewService(IAppDbContext db) : ICheckoutPreviewService
{
    public async Task<CheckoutPreviewDto> PreviewAsync(Guid userId, CancellationToken ct = default)
    {
        var cart = await db.Carts.AsNoTracking().Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (cart is null || cart.Items.Count == 0)
            throw new EmptyCartException();

        var productIds = cart.Items.Select(i => i.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Include(p => p.Inventory)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var issues = new List<string>();
        var lines = new List<CheckoutPreviewLineDto>(cart.Items.Count);

        foreach (var line in cart.Items.OrderBy(i => i.CreatedAt).ThenBy(i => i.Id))
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                issues.Add($"Cart line {line.Id} references a product that no longer exists.");
                continue;
            }

            if (product.Status != Shop.Domain.Enums.ProductStatus.Active)
                issues.Add($"{product.Name} is no longer available for sale.");

            if (line.Quantity <= 0)
                issues.Add($"{product.Name} has a non-positive quantity.");

            var available = product.Inventory is null
                ? 0
                : product.Inventory.Quantity - product.Inventory.ReservedQuantity;
            if (line.Quantity > 0 && available < line.Quantity)
                issues.Add($"{product.Name}: only {available} available, {line.Quantity} requested.");

            lines.Add(new CheckoutPreviewLineDto(
                product.Id,
                product.Name,
                product.SKU,
                product.Price, // pricing policy: the server price, never the cart snapshot
                line.Quantity,
                available,
                product.Price * line.Quantity,
                line.Quantity > 0 && available >= line.Quantity));
        }

        var subtotal = lines.Sum(l => l.LineTotal);
        var shipping = CheckoutPricing.ShippingFeeFor(subtotal);

        return new CheckoutPreviewDto(
            lines,
            subtotal,
            shipping,
            subtotal + shipping,
            issues.Count == 0,
            issues);
    }
}
