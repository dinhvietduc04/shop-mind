using Microsoft.EntityFrameworkCore;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Domain.Events;
using Shop.Domain.Exceptions;

namespace Shop.Application.Services;

/// <summary>
/// B5: the one money path. Creates a Quote (ADR-0001: a Pending Order with
/// expiry) from the cart — server-repriced, stock reserved via B1 guarded
/// primitives, atomically replacing any existing Open Quote (release + re-reserve
/// in one transaction) — plus a single-use PaymentSession whose 128-bit token
/// is the pay-page capability (ADR-0002). The cart is NOT cleared here; the
/// human may still tweak it (a later quote replaces this one), and capture
/// clears the quoted lines.
/// </summary>
public class QuoteService(
    IAppDbContext db,
    IInventoryService inventory,
    IEventPublisher events,
    OrderStatusTransitioner transitioner) : IQuoteService
{
    public const int QuoteExpiryMinutes = 30; // reservation TTL (plan: 30m, sweeper every 30s)
    private const decimal FreeShippingThreshold = 500m;
    private const decimal FlatShippingFee = 15m;

    public async Task<QuoteResult> CreateQuoteAsync(Guid userId, CreateQuoteRequest request, CancellationToken ct = default)
    {
        var cart = await db.Carts.AsNoTracking().Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (cart is null || cart.Items.Count == 0)
            throw new EmptyCartException();

        var productIds = cart.Items.Select(i => i.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        foreach (var line in cart.Items)
        {
            if (!products.TryGetValue(line.ProductId, out var p))
                throw new ProductNotFoundException(line.ProductId);
            if (p.Status != ProductStatus.Active)
                throw new InactiveProductException(p.Id);
            if (line.Quantity <= 0)
                throw new DomainValidationException("Quantity must be greater than zero.");
        }

        // One transaction: replace-old + re-reserve + order + session + event.
        // Any guarded failure (stock gone, racing writer) rolls back everything —
        // no half-swapped quotes, no orphan reservations.
        await using var tx = await db.BeginTransactionAsync(ct);

        var openQuotes = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.UserId == userId && o.Status == OrderStatus.Pending && o.ExpiresAt != null)
            .ToListAsync(ct);
        foreach (var old in openQuotes)
        {
            var outcome = await transitioner.ApplyAsync(old.Id, OrderStatus.Pending, OrderStatus.Cancelled, ct);
            if (outcome == OrderStatusTransitioner.Outcome.Transitioned)
            {
                foreach (var item in old.Items)
                    if (item.ReservedQuantity > 0)
                        await inventory.TryReleaseReservationAsync(item.ProductId, item.ReservedQuantity, ct);
                await events.EnqueueAsync(new OrderCancelledEvent(old.Id, userId, "replaced by new quote"), ct);
            }
        }

        var order = new Order
        {
            UserId = userId,
            OrderNumber = Order.GenerateOrderNumber(),
            Status = OrderStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(QuoteExpiryMinutes),
            ShippingAddress = new Shop.Domain.ValueObjects.ShippingAddress
            {
                FullName = request.ShippingAddress.FullName,
                Phone = request.ShippingAddress.Phone,
                Address = request.ShippingAddress.Address,
                City = request.ShippingAddress.City,
            },
        };
        foreach (var line in cart.Items)
        {
            var p = products[line.ProductId];
            var reserved = await inventory.TryReserveAsync(p.Id, line.Quantity, ct);
            if (!reserved)
            {
                var inv = await db.Inventories.AsNoTracking().FirstOrDefaultAsync(i => i.ProductId == p.Id, ct);
                throw new InsufficientStockException(p.Id,
                    inv is null ? 0 : inv.Quantity - inv.ReservedQuantity, line.Quantity);
            }
            order.Items.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductId = p.Id,
                ProductName = p.Name,
                SKU = p.SKU,
                UnitPrice = p.Price, // pricing policy: server price, never the client's
                Quantity = line.Quantity,
                ReservedQuantity = line.Quantity,
                Subtotal = p.Price * line.Quantity,
            });
        }
        order.Subtotal = order.Items.Sum(i => i.Subtotal);
        order.ShippingFee = order.Subtotal >= FreeShippingThreshold || order.Subtotal == 0 ? 0m : FlatShippingFee;
        order.TotalAmount = order.Subtotal + order.ShippingFee;

        db.Orders.Add(order);

        var sessionToken = PaymentSession.GenerateSessionToken();
        db.PaymentSessions.Add(new PaymentSession
        {
            SessionToken = sessionToken,
            Provider = PaymentProvider.FakePsp,
            OrderId = order.Id,
            UserId = userId,
            Amount = order.TotalAmount,
        });

        await events.EnqueueAsync(new OrderCreatedEvent(order.Id, userId, order.TotalAmount, order.Items.Count), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await BuildResultAsync(order.Id, ct);
    }

    public async Task<QuoteResult?> GetOpenQuoteAsync(Guid userId, CancellationToken ct = default)
    {
        var open = await db.Orders.AsNoTracking()
            .Where(o => o.UserId == userId && o.Status == OrderStatus.Pending && o.ExpiresAt != null)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(ct);
        return open is Guid id ? await BuildResultAsync(id, ct) : null;
    }

    private async Task<QuoteResult> BuildResultAsync(Guid orderId, CancellationToken ct)
    {
        var saved = await db.Orders.AsNoTracking().Include(o => o.Items)
            .FirstAsync(o => o.Id == orderId, ct);
        var session = await db.PaymentSessions.AsNoTracking().FirstAsync(s => s.OrderId == orderId, ct);
        return new QuoteResult(saved.ToDto(), $"/pay/{session.SessionToken}", session.SessionToken, saved.ExpiresAt!.Value);
    }
}
