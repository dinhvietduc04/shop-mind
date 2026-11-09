using Microsoft.EntityFrameworkCore;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;

namespace Shop.Application.Services;

public class CartService(IAppDbContext db) : ICartService
{
    private async Task<Cart> EnsureCartAsync(Guid userId, CancellationToken ct)
    {
        var cart = await db.Carts.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (cart is not null) return cart;

        // JWT is valid but user row is gone (e.g. DB volume was wiped and reseeded
        // with new GUIDs while frontend still holds an old token). Fail fast with
        // 401 instead of hitting FK_Carts_Users_UserId on INSERT.
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
            throw new UnauthorizedAccessException("Session expired (user no longer exists). Please login again.");

        cart = new Cart { UserId = userId };
        db.Carts.Add(cart);
        await db.SaveChangesAsync(ct);
        return cart;
    }

    private async Task<CartDto> LoadDtoAsync(Guid userId, CancellationToken ct)
    {
        var cart = await db.Carts
            .AsNoTracking()
            .Include(c => c.Items).ThenInclude(i => i.Product).ThenInclude(p => p!.Images)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? new Cart { UserId = userId, Items = new List<CartItem>() };
        return cart.ToDto();
    }

    public async Task<CartDto> GetCartAsync(Guid userId, CancellationToken ct = default)
        => await LoadDtoAsync(userId, ct);

    public async Task<CartDto> AddItemAsync(Guid userId, AddCartItemRequest request, CancellationToken ct = default)
    {
        if (request.Quantity <= 0) throw new DomainValidationException("Quantity must be greater than zero.");

        // B1: concurrent same-user adds collide on xmin (or on unique constraints when
        // two attempts insert the same row); reload-and-retry keeps the read-modify-write
        // below from dropping quantities (agent retries hammer this path).
        // Budget sized for the plan's exit-criterion storm (10 parallel adds, each
        // colliding on CartItem AND Cart header).
        const int maxRetries = 8;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await AddItemOnceAsync(userId, request, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxRetries)
            {
                await BackoffAsync(attempt, ct);
                db.ClearChangeTracker(); // next read sees database truth
            }
            catch (DbUpdateException ex) when (attempt < maxRetries && ex is not DbUpdateConcurrencyException)
            {
                // Lost an insert race (unique constraint): the row now exists —
                // re-read and apply this add on top of it.
                await BackoffAsync(attempt, ct);
                db.ClearChangeTracker();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new DomainValidationException(
                    "Cart was modified concurrently, please retry.");
            }
        }
    }

    // Hot-loop retries immediately re-collide under a storm; exponential + jittered
    // backoff lets the writers serialize.
    private static Task BackoffAsync(int attempt, CancellationToken ct)
        => Task.Delay(TimeSpan.FromMilliseconds(5 * Math.Pow(2, attempt) + Random.Shared.Next(10)), ct);

    private async Task<CartDto> AddItemOnceAsync(Guid userId, AddCartItemRequest request, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Include(p => p.Inventory)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, ct)
            ?? throw new ProductNotFoundException(request.ProductId);
        if (product.Status != ProductStatus.Active) throw new InactiveProductException(product.Id);

        var cart = await EnsureCartAsync(userId, ct);

        var existing = await db.CartItems
            .FirstOrDefaultAsync(i => i.CartId == cart.Id && i.ProductId == request.ProductId, ct);

        var newQty = (existing?.Quantity ?? 0) + request.Quantity;
        var available = product.Inventory is null ? 0 : product.Inventory.Quantity - product.Inventory.ReservedQuantity;
        if (available < newQty)
            throw new InsufficientStockException(product.Id, available, newQty);

        if (existing is null)
        {
            db.CartItems.Add(new CartItem
            {
                CartId = cart.Id,
                ProductId = product.Id,
                Quantity = request.Quantity,
                UnitPrice = product.Price // pricing policy: snapshot current price, refreshed on checkout
            });
        }
        else
        {
            existing.Quantity = newQty;
            existing.UnitPrice = product.Price;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await LoadDtoAsync(userId, ct);
    }

    public async Task<CartDto> UpdateItemAsync(Guid userId, Guid itemId, UpdateCartItemRequest request, CancellationToken ct = default)
    {
        if (request.Quantity <= 0) throw new DomainValidationException("Quantity must be greater than zero.");

        var item = await db.CartItems
            .Include(i => i.Cart)
            .FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new CartItemNotFoundException(itemId);
        if (item.Cart is null || item.Cart.UserId != userId)
            throw new CartItemNotFoundException(itemId);

        var product = await db.Products.AsNoTracking()
            .Include(p => p.Inventory)
            .FirstOrDefaultAsync(p => p.Id == item.ProductId, ct)
            ?? throw new ProductNotFoundException(item.ProductId);
        if (product.Status != ProductStatus.Active) throw new InactiveProductException(product.Id);
        var available = product.Inventory is null ? 0 : product.Inventory.Quantity - product.Inventory.ReservedQuantity;
        if (available < request.Quantity)
            throw new InsufficientStockException(product.Id, available, request.Quantity);

        item.Quantity = request.Quantity;
        item.UnitPrice = product.Price;
        item.UpdatedAt = DateTime.UtcNow;
        item.Cart.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await LoadDtoAsync(userId, ct);
    }

    public async Task<CartDto> RemoveItemAsync(Guid userId, Guid itemId, CancellationToken ct = default)
    {
        var item = await db.CartItems
            .Include(i => i.Cart)
            .FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new CartItemNotFoundException(itemId);
        if (item.Cart is null || item.Cart.UserId != userId)
            throw new CartItemNotFoundException(itemId);

        db.CartItems.Remove(item);
        item.Cart.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await LoadDtoAsync(userId, ct);
    }

    public async Task ClearAsync(Guid userId, CancellationToken ct = default)
    {
        var cart = await db.Carts.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (cart is null) return;
        var items = await db.CartItems.Where(i => i.CartId == cart.Id).ToListAsync(ct);
        foreach (var i in items) db.CartItems.Remove(i);
        cart.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
