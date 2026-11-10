using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shop.Application.Interfaces;
using Shop.Infrastructure.Payments;

namespace Shop.Api.Controllers;

/// <summary>
/// B5: PSP webhook intake. Verify the adapter signature over the RAW body,
/// normalize to the locked provider-event contract, enqueue for the worker,
/// acknowledge 202. Applying money fates here (inline) would put a second
/// write path in the request pipeline — forbidden by the plan; the consumer
/// owns application + dedupe.
/// </summary>
[ApiController]
[Route("api/payments")]
public class PaymentsWebhookController(FakePspProvider fakePsp, Shop.Application.Persistence.IAppDbContext db) : ControllerBase
{
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(ct);
        var signature = Request.Headers["X-Psp-Signature"].ToString();

        // v1: FakePsp is the only adapter; the Stripe milestone adds provider
        // routing by header, each with its own verification.
        var evt = fakePsp.VerifyAndNormalize(rawBody, signature);
        await fakePsp.EnqueueIntakeAsync(evt, ct);
        await db.SaveChangesAsync(ct); // persist the intake outbox row the publisher enqueued
        return Accepted(new { received = true });
    }
}
