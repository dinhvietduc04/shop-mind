using Microsoft.EntityFrameworkCore;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;

namespace Shop.Application.Services;

public class CheckoutService(IAppDbContext db, IFakePaymentService payments) : ICheckoutService
{
    private const decimal FreeShippingThreshold = 500m;
    private const decimal FlatShippingFee = 15m;

    public async Task<CheckoutResponse> CheckoutAsync(Guid userId, CheckoutRequest request, CancellationToken ct = default)
    {
        var cart = await db.Carts.Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (cart is null || cart.Items.Count == 0)
            throw new EmptyCartException();

        await using var tx = await db.BeginTransactionAsync(ct);
        try
        {
            // Re-validate products, prices, stock inside transaction
            var productIds = cart.Items.Select(i => i.ProductId).ToList();
            var products = await db.Products.Include(p => p.Inventory)
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

            foreach (var item in cart.Items)
            {
                if (!products.TryGetValue(item.ProductId, out var product))
                    throw new ProductNotFoundException(item.ProductId);
                if (product.Status != ProductStatus.Active)
                    throw new InactiveProductException(product.Id);
                if (item.Quantity <= 0)
                    throw new DomainValidationException("Quantity must be greater than zero.");
                var available = product.Inventory is null ? 0 : product.Inventory.Quantity - product.Inventory.ReservedQuantity;
                if (available < item.Quantity)
                    throw new InsufficientStockException(product.Id, available, item.Quantity);
                // Pricing policy: backend recalculates from current product price; never trust client price
                item.UnitPrice = product.Price;
            }

            var subtotal = cart.Items.Sum(i => i.UnitPrice * i.Quantity);
            var shippingFee = subtotal >= FreeShippingThreshold || subtotal == 0 ? 0m : FlatShippingFee;
            var total = subtotal + shippingFee;

            var order = new Order
            {
                UserId = userId,
                OrderNumber = Order.GenerateOrderNumber(),
                Status = OrderStatus.Pending,
                Subtotal = subtotal,
                ShippingFee = shippingFee,
                TotalAmount = total,
                ShippingAddress = request.ShippingAddress.ToVo(),
            };
            foreach (var item in cart.Items)
            {
                var p = products[item.ProductId];
                order.Items.Add(new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = p.Id,
                    ProductName = p.Name,
                    SKU = p.SKU,
                    UnitPrice = item.UnitPrice,
                    Quantity = item.Quantity,
                    Subtotal = item.UnitPrice * item.Quantity
                });
            }
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct); // need order persisted before payment

            // Fake payment (inside transaction conceptually; rollback on failure path keeps consistency)
            var result = await payments.ProcessAsync(order.Id, total, request.SimulateFailure, request.SimulateTimeout, ct);

            var payment = new Payment
            {
                OrderId = order.Id,
                Amount = total,
                Status = result.Succeeded ? PaymentStatus.Succeeded : PaymentStatus.Failed,
                TransactionId = result.TransactionId,
            };
            db.Payments.Add(payment);

            if (!result.Succeeded)
            {
                order.Status = OrderStatus.PaymentFailed;
                order.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                throw new PaymentFailedException(order.Id, result.FailureReason ?? "Fake payment declined.");
            }

            order.Status = OrderStatus.Paid;
            order.UpdatedAt = DateTime.UtcNow;

            // Update inventory (most important validation already done; decrement atomically)
            foreach (var item in cart.Items)
            {
                var product = products[item.ProductId];
                product.Inventory!.DecreaseStock(item.Quantity);
            }

            // Clear cart
            foreach (var item in cart.Items.ToList()) db.CartItems.Remove(item);
            cart.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // reload for response
            var saved = await db.Orders.Include(o => o.Items).Include(o => o.Payment)
                .FirstAsync(o => o.Id == order.Id, ct);
            return new CheckoutResponse(saved.ToDto(), saved.Payment!.ToDto());
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* already committed (payment-failed path) */ }
            throw;
        }
    }
}
