using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shop.Api.Extensions;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;

namespace Shop.Api.Controllers;

/// <summary>
/// B5 (ADR-0002): the hosted pay page. GET is authorized by the capability
/// token alone (read-only invoice view — an agent may read it). POST confirm
/// additionally requires the HUMAN's JWT: owner match, never scope=agent.
/// Confirm does not mark anything paid — it asks the provider to issue its
/// webhook; the consumer applies the money fate (one money path, ADR-0003).
/// </summary>
[ApiController]
[Route("pay")]
public class PayController(IAppDbContext db, IPaymentProvider provider) : ControllerBase
{
    [HttpGet("{sessionToken}")]
    [AllowAnonymous]
    public async Task<ActionResult<PayBillView>> ViewBill(string sessionToken, CancellationToken ct)
    {
        var session = await db.PaymentSessions.AsNoTracking()
            .Include(s => s.Order!).ThenInclude(o => o.Items)
            .FirstOrDefaultAsync(s => s.SessionToken == sessionToken, ct);
        if (session?.Order is null)
            return NotFound();

        var o = session.Order;
        return Ok(new PayBillView(
            session.Id,
            session.Provider.ToString(),
            o.OrderNumber,
            o.Subtotal,
            o.ShippingFee,
            o.TotalAmount,
            session.Currency,
            o.Status.ToString(),
            o.Outcome is { } outcome ? (PaymentOutcomeDto)outcome : null,
            o.ExpiresAt ?? o.CreatedAt,
            o.Items.Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.SKU, i.UnitPrice, i.Quantity, i.Subtotal)).ToList()));
    }

    [HttpPost("{sessionToken}/confirm")]
    [Authorize]
    public async Task<IActionResult> Confirm(string sessionToken, CancellationToken ct)
    {
        // Money-acting endpoints reject agent scope at the auth edge (ADR-0006).
        if (User.IsAgentToken())
            throw new AgentScopeForbiddenException("payment confirmation");

        var session = await db.PaymentSessions.AsNoTracking()
            .Include(s => s.Order!)
            .FirstOrDefaultAsync(s => s.SessionToken == sessionToken, ct);
        if (session?.Order is null)
            return NotFound();

        var userId = User.GetUserId();
        if (session.UserId != userId)
            throw new UnauthorizedOrderAccessException();
        if (session.Order.Status != OrderStatus.Pending)
            throw new InvalidOrderStateException(session.Order.Id, session.Order.Status, OrderStatus.Paid);
        if (session.Order.ExpiresAt is { } exp && exp <= DateTime.UtcNow)
            throw new InvalidOrderStateException(session.Order.Id, session.Order.Status, OrderStatus.Paid); // past expiry: sweeper/webhook "expired" owns the fate

        var view = new PaySessionView(session.Id, session.OrderId, session.UserId, session.SessionToken, session.Amount, session.Currency, ProviderName(session.Provider));
        await provider.IssueSuccessWebhookAsync(view, ct);
        await db.SaveChangesAsync(ct); // the enqueue added an intake outbox row on this scope's context

        // 202 semantics without claiming money moved: the webhook decides.
        return Accepted(new { status = "awaiting-payment-confirmation" });
    }

    private static string ProviderName(PaymentProvider p) => p switch
    {
        PaymentProvider.FakePsp => "fake-psp",
        _ => p.ToString().ToLowerInvariant(),
    };
}
