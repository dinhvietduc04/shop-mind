using Microsoft.EntityFrameworkCore;
using Shop.Application.Caching;
using Shop.Application.Common;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Exceptions;

namespace Shop.Application.Services;

public class InventoryService(IAppDbContext db, ICacheService cache) : IInventoryService
{
    public async Task<PagedResult<InventoryDto>> ListAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var q = db.Inventories.Include(i => i.Product).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(i => i.Product != null && (i.Product.Name.ToLower().Contains(s) || i.Product.SKU.ToLower().Contains(s)));
        }
        q = q.OrderBy(i => i.Product!.Name);
        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<InventoryDto> { Items = items.Select(Mapping.ToDto).ToList(), Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<InventoryDto> GetByProductIdAsync(Guid productId, CancellationToken ct = default)
    {
        var inv = await db.Inventories.Include(i => i.Product).FirstOrDefaultAsync(i => i.ProductId == productId, ct)
            ?? throw new ProductNotFoundException(productId);
        return inv.ToDto();
    }

    public async Task<InventoryDto> UpdateQuantityAsync(Guid productId, int quantity, CancellationToken ct = default)
    {
        if (quantity < 0) throw new DomainValidationException("Quantity cannot be negative.");
        var inv = await db.Inventories.Include(i => i.Product).FirstOrDefaultAsync(i => i.ProductId == productId, ct)
            ?? throw new ProductNotFoundException(productId);
        inv.SetQuantity(quantity);
        await db.SaveChangesAsync(ct);
        // M9: stock change → evict product:{id} (+ query listings that embed
        // AvailableStock / InStockOnly). Checkout revalidates live stock so a
        // briefly stale cache can never oversell.
        await cache.RemoveAsync(CacheKeys.Product(productId), ct);
        await cache.RemoveByPrefixAsync(CacheKeys.ProductsQueryPrefix, ct);
        return inv.ToDto();
    }
}
