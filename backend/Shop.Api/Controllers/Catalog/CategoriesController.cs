using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shop.Application.Caching;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;

namespace Shop.Api.Controllers;

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
