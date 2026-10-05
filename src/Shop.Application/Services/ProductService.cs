using Microsoft.EntityFrameworkCore;
using Shop.Application.Common;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Application.Validation;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;

namespace Shop.Application.Services;

public class ProductService(IAppDbContext db) : IProductService
{
    public async Task<PagedResult<ProductDto>> QueryAsync(ProductQueryParams query, CancellationToken ct = default)
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize is <= 0 or > 100 ? 20 : query.PageSize;

        var q = db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Inventory)
            .AsQueryable();

        if (!query.IncludeInactive)
            q = q.Where(p => p.Status == ProductStatus.Active);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            q = q.Where(p => p.Name.ToLower().Contains(s) || p.SKU.ToLower().Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var c = query.Category.Trim().ToLower();
            if (Guid.TryParse(c, out var catId))
                q = q.Where(p => p.CategoryId == catId);
            else
                q = q.Where(p => p.Category != null && p.Category.Slug == c);
            // inactive categories hidden for customers
            if (!query.IncludeInactive)
                q = q.Where(p => p.Category == null || p.Category.IsActive);
        }
        if (!string.IsNullOrWhiteSpace(query.Brand))
        {
            var b = query.Brand.Trim().ToLower();
            q = q.Where(p => p.Brand != null && p.Brand.ToLower() == b);
        }
        if (query.MinPrice.HasValue) q = q.Where(p => p.Price >= query.MinPrice.Value);
        if (query.MaxPrice.HasValue) q = q.Where(p => p.Price <= query.MaxPrice.Value);
        if (query.InStockOnly == true) q = q.Where(p => p.Inventory != null && p.Inventory.Quantity - p.Inventory.ReservedQuantity > 0);

        q = query.Sort?.ToLowerInvariant() switch
        {
            "price_asc" => q.OrderBy(p => p.Price),
            "price_desc" => q.OrderByDescending(p => p.Price),
            "newest" => q.OrderByDescending(p => p.CreatedAt),
            "name_desc" => q.OrderByDescending(p => p.Name),
            _ => q.OrderBy(p => p.Name),
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<ProductDto>
        {
            Items = items.Select(Mapping.ToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
    }

    public async Task<ProductDetailDto> GetByIdAsync(Guid id, bool includeInactive = false, CancellationToken ct = default)
    {
        var p = await db.Products.Include(x => x.Category).Include(x => x.Images).Include(x => x.Inventory)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ProductNotFoundException(id);
        if (!includeInactive && p.Status != ProductStatus.Active)
            throw new ProductNotFoundException(id);
        return p.ToDetailDto();
    }

    public async Task<PagedResult<ProductDto>> ListAdminAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var q = db.Products.Include(p => p.Category).Include(p => p.Images).Include(p => p.Inventory).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(p => p.Name.ToLower().Contains(s) || p.SKU.ToLower().Contains(s));
        }
        q = q.OrderByDescending(p => p.CreatedAt);
        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<ProductDto> { Items = items.Select(Mapping.ToDto).ToList(), Page = page, PageSize = pageSize, TotalCount = total };
    }

    public async Task<ProductDetailDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default)
    {
        if (await db.Products.AnyAsync(p => p.SKU == request.SKU.Trim(), ct))
            throw new DomainValidationException($"SKU '{request.SKU}' already exists.");
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct)
            ?? throw new CategoryNotFoundException(request.CategoryId);
        var slug = string.IsNullOrWhiteSpace(request.Slug) ? SlugHelper.Slugify(request.Name) : SlugHelper.Slugify(request.Slug);
        if (await db.Products.AnyAsync(p => p.Slug == slug, ct))
            throw new DomainValidationException($"Slug '{slug}' already exists.");
        if (request.Price < 0) throw new DomainValidationException("Price must be greater than or equal to zero.");
        if (request.InitialStock < 0) throw new DomainValidationException("Initial stock cannot be negative.");

        var product = new Product
        {
            Name = request.Name.Trim(),
            Slug = slug,
            SKU = request.SKU.Trim(),
            CategoryId = category.Id,
            Description = request.Description?.Trim(),
            Brand = request.Brand?.Trim(),
            Price = request.Price,
            Status = request.Status,
        };
        if (request.Images is not null)
            foreach (var img in request.Images.OrderBy(i => i.DisplayOrder))
                product.Images.Add(new ProductImage { ProductId = product.Id, Url = img.Url.Trim(), AltText = img.AltText?.Trim(), DisplayOrder = img.DisplayOrder });
        product.Inventory = new Inventory { ProductId = product.Id, Quantity = request.InitialStock, ReservedQuantity = 0 };
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(product.Id, true, ct);
    }

    public async Task<ProductDetailDto> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken ct = default)
    {
        var product = await db.Products.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new ProductNotFoundException(id);
        if (await db.Products.AnyAsync(p => p.Id != id && p.SKU == request.SKU.Trim(), ct))
            throw new DomainValidationException($"SKU '{request.SKU}' already exists.");
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct)
            ?? throw new CategoryNotFoundException(request.CategoryId);
        var slug = string.IsNullOrWhiteSpace(request.Slug) ? SlugHelper.Slugify(request.Name) : SlugHelper.Slugify(request.Slug);
        if (await db.Products.AnyAsync(p => p.Id != id && p.Slug == slug, ct))
            throw new DomainValidationException($"Slug '{slug}' already exists.");
        if (request.Price < 0) throw new DomainValidationException("Price must be greater than or equal to zero.");

        product.Name = request.Name.Trim();
        product.Slug = slug;
        product.SKU = request.SKU.Trim();
        product.CategoryId = category.Id;
        product.Description = request.Description?.Trim();
        product.Brand = request.Brand?.Trim();
        product.Price = request.Price;
        product.Status = request.Status;
        product.UpdatedAt = DateTime.UtcNow;

        if (request.Images is not null)
        {
            var toRemove = product.Images.ToList();
            foreach (var r in toRemove) db.ProductImages.Remove(r);
            product.Images.Clear();
            foreach (var img in request.Images.OrderBy(i => i.DisplayOrder))
                product.Images.Add(new ProductImage { ProductId = product.Id, Url = img.Url.Trim(), AltText = img.AltText?.Trim(), DisplayOrder = img.DisplayOrder });
        }
        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(product.Id, true, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new ProductNotFoundException(id);
        // Soft-delete: mark inactive to preserve order history
        product.Status = ProductStatus.Inactive;
        product.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
