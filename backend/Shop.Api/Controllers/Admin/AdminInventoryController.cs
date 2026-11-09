using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;

namespace Shop.Api.Controllers;

[ApiController]
[Route("api/admin/inventory")]
[Authorize(Roles = "Admin")]
public class AdminInventoryController(IInventoryService inventory) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, CancellationToken ct = default)
        => Ok(await inventory.ListAsync(page, pageSize, search, ct));

    [HttpGet("{productId:guid}")]
    public async Task<ActionResult<InventoryDto>> Get(Guid productId, CancellationToken ct)
        => Ok(await inventory.GetByProductIdAsync(productId, ct));

    [HttpPatch("{productId:guid}")]
    public async Task<ActionResult<InventoryDto>> Update(Guid productId, [FromBody] UpdateInventoryRequest request, CancellationToken ct)
        => Ok(await inventory.UpdateQuantityAsync(productId, request.Quantity, ct));
}
