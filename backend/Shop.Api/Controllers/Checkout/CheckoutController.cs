using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shop.Api.Extensions;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;

namespace Shop.Api.Controllers;

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
