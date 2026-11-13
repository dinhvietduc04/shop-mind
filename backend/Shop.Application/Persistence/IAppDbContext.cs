using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Shop.Domain.Entities;

namespace Shop.Application.Persistence;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<Inventory> Inventories { get; }
    DbSet<Cart> Carts { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentSession> PaymentSessions { get; }
    DbSet<PaymentAnomaly> PaymentAnomalies { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<AiConversation> AiConversations { get; }
    DbSet<AiMessage> AiMessages { get; }
    DbSet<AiAction> AiActions { get; }

    /// <summary>B5: per-consumer exactly-once ledger (webhook intake + worker consumers).</summary>
    DbSet<ProcessedEvent> ProcessedEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// B1: drop tracked entities so the next read sees database truth. Used by
    /// reload-and-retry loops handling DbUpdateConcurrencyException — the tracked
    /// instances held by a failed attempt carry stale values and stale xmin tokens.
    /// </summary>
    void ClearChangeTracker();
}
