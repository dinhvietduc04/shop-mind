using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shop.Application.Caching;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;

namespace Shop.Api.Controllers;

[ApiController]
[Route("api/products")]
[AllowAnonymous]
public class ProductsController(
    IProductService products,
    ICacheService cache,
    IOptionsMonitor<RedisOptions> redis) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Query(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? brand,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] bool? inStockOnly,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = new ProductQueryParams(search, category, brand, minPrice, maxPrice, inStockOnly, sort, page, pageSize);
        if (IsBypassRequested())
        {
            Response.Headers["X-Cache"] = "BYPASSED";
            return Ok(await products.QueryAsync(query, ct));
        }

        var ttlSeconds = redis.CurrentValue.DefaultTtlSeconds;
        var ttl = TimeSpan.FromSeconds(ttlSeconds > 0 ? ttlSeconds : 90);
        var (result, fromCache) = await cache.GetOrCreateAsync(
            CacheKeys.ProductsQuery(query),
            () => products.QueryAsync(query, ct),
            ttl,
            ct);
        Response.Headers["X-Cache"] = fromCache ? "HIT" : "MISS";
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDetailDto>> GetById(Guid id, CancellationToken ct)
    {
        if (IsBypassRequested())
        {
            Response.Headers["X-Cache"] = "BYPASSED";
            return Ok(await products.GetByIdAsync(id, false, ct));
        }

        var (result, fromCache) = await cache.GetOrCreateAsync(
            CacheKeys.Product(id),
            () => products.GetByIdAsync(id, false, ct),
            CacheDurations.Product,
            ct);
        Response.Headers["X-Cache"] = fromCache ? "HIT" : "MISS";
        return Ok(result);
    }

    /// <summary>M9: cache bypass is Admin-only; non-admins get normal cached reads.</summary>
    private bool IsBypassRequested()
    {
        var requested = Request.Query.ContainsKey("nocache") || Request.Headers.ContainsKey("X-Bypass-Cache");
        return requested && User.IsInRole("Admin");
    }
}

[ApiController]
[Route("api/categories")]
[AllowAnonymous]
public class CategoriesController(
    ICategoryService categories,
    ICacheService cache) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> List(CancellationToken ct)
    {
        if (IsBypassRequested())
        {
            Response.Headers["X-Cache"] = "BYPASSED";
            return Ok(await categories.ListPublicAsync(ct));
        }

        var (result, fromCache) = await cache.GetOrCreateAsync(
            CacheKeys.CategoriesAll,
            () => categories.ListPublicAsync(ct),
            CacheDurations.Categories,
            ct);
        Response.Headers["X-Cache"] = fromCache ? "HIT" : "MISS";
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> GetById(Guid id, CancellationToken ct)
        // M9: single-category read stays uncached (only the public list is hot).
        => Ok(await categories.GetByIdAsync(id, ct));

    private bool IsBypassRequested()
    {
        var requested = Request.Query.ContainsKey("nocache") || Request.Headers.ContainsKey("X-Bypass-Cache");
        return requested && User.IsInRole("Admin");
    }
}
