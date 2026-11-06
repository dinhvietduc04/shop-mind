using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shop.Api.Extensions;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;

namespace Shop.Api.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize]
public class CartController(ICartService carts, IValidator<AddCartItemRequest> addValidator, IValidator<UpdateCartItemRequest> updateValidator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CartDto>> Get(CancellationToken ct)
        => Ok(await carts.GetCartAsync(User.GetUserId(), ct));

    [HttpPost("items")]
    public async Task<ActionResult<CartDto>> Add([FromBody] AddCartItemRequest request, CancellationToken ct)
    {
        var v = await addValidator.ValidateAsync(request, ct);
        if (!v.IsValid) throw new ValidationException(v.Errors);
        var cart = await carts.AddItemAsync(User.GetUserId(), request, ct);
        return Ok(cart);
    }

    [HttpPatch("items/{itemId:guid}")]
    public async Task<ActionResult<CartDto>> Update(Guid itemId, [FromBody] UpdateCartItemRequest request, CancellationToken ct)
    {
        var v = await updateValidator.ValidateAsync(request, ct);
        if (!v.IsValid) throw new ValidationException(v.Errors);
        return Ok(await carts.UpdateItemAsync(User.GetUserId(), itemId, request, ct));
    }

    [HttpDelete("items/{itemId:guid}")]
    public async Task<ActionResult<CartDto>> Remove(Guid itemId, CancellationToken ct)
        => Ok(await carts.RemoveItemAsync(User.GetUserId(), itemId, ct));

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await carts.ClearAsync(User.GetUserId(), ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/checkout")]
[Authorize]
public class CheckoutController(ICheckoutService checkout, IValidator<CheckoutRequest> validator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CheckoutResponse>> Checkout([FromBody] CheckoutRequest request, CancellationToken ct)
    {
        var v = await validator.ValidateAsync(request, ct);
        if (!v.IsValid) throw new ValidationException(v.Errors);
        return Ok(await checkout.CheckoutAsync(User.GetUserId(), request, ct));
    }
}

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController(IOrderService orders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await orders.ListMineAsync(User.GetUserId(), page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken ct)
        => Ok(await orders.GetMineByIdAsync(User.GetUserId(), id, ct));

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<OrderDto>> Cancel(Guid id, CancellationToken ct)
        => Ok(await orders.CancelMineAsync(User.GetUserId(), id, ct));
}
