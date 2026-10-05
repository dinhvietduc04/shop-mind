using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Domain.Enums;

namespace Shop.Api.Controllers;

[ApiController]
[Route("api/admin/products")]
[Authorize(Roles = "Admin")]
public class AdminProductsController(
    IProductService products,
    IValidator<CreateProductRequest> createValidator,
    IValidator<UpdateProductRequest> updateValidator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await products.ListAdminAsync(search, page, pageSize, ct));

    [HttpPost]
    public async Task<ActionResult<ProductDetailDto>> Create([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        var v = await createValidator.ValidateAsync(request, ct);
        if (!v.IsValid) throw new ValidationException(v.Errors);
        var created = await products.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDetailDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await products.GetByIdAsync(id, true, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductDetailDto>> Update(Guid id, [FromBody] UpdateProductRequest request, CancellationToken ct)
    {
        var v = await updateValidator.ValidateAsync(request, ct);
        if (!v.IsValid) throw new ValidationException(v.Errors);
        return Ok(await products.UpdateAsync(id, request, ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/admin/categories")]
[Authorize(Roles = "Admin")]
public class AdminCategoriesController(
    ICategoryService categories,
    IValidator<CreateCategoryRequest> createValidator,
    IValidator<UpdateCategoryRequest> updateValidator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> List(CancellationToken ct)
        => Ok(await categories.ListAdminAsync(ct));

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create([FromBody] CreateCategoryRequest request, CancellationToken ct)
    {
        var v = await createValidator.ValidateAsync(request, ct);
        if (!v.IsValid) throw new ValidationException(v.Errors);
        var created = await categories.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await categories.GetByIdAsync(id, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> Update(Guid id, [FromBody] UpdateCategoryRequest request, CancellationToken ct)
    {
        var v = await updateValidator.ValidateAsync(request, ct);
        if (!v.IsValid) throw new ValidationException(v.Errors);
        return Ok(await categories.UpdateAsync(id, request, ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return NoContent();
    }
}

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

[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = "Admin")]
public class AdminOrdersController(IOrderService orders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null, CancellationToken ct = default)
        => Ok(await orders.ListAdminAsync(page, pageSize, status, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken ct)
        => Ok(await orders.GetAdminByIdAsync(id, ct));

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<OrderDto>> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<OrderStatus>(request.Status, true, out var status))
            throw new FluentValidation.ValidationException([new FluentValidation.Results.ValidationFailure("status", $"Invalid status '{request.Status}'.")]);
        return Ok(await orders.UpdateStatusAdminAsync(id, status, ct));
    }
}
