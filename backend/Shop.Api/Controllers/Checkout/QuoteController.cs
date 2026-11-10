using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shop.Api.Extensions;
using Shop.Api.Idempotency;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;

namespace Shop.Api.Controllers;

/// <summary>
/// B5 (ADR-0001/0003): the only money POST. Requires an Idempotency-Key (B2 —
/// agent-retry safe), falls in the 6/min checkout/quote rate bucket (B3), and
/// refuses agent-scope tokens (ADR-0006 — agents may quote-read but never act
/// on money surfaces). MCP create_quote (A2) calls the same IQuoteService.
/// </summary>
[ApiController]
[Route("api/checkout")]
[Authorize]
public class QuoteController(IQuoteService quotes, IValidator<CreateQuoteRequest> validator) : ControllerBase
{
    [HttpPost("quote")]
    [RequireIdempotency]
    public async Task<ActionResult<QuoteResult>> CreateQuote([FromBody] CreateQuoteRequest request, CancellationToken ct)
    {
        if (User.IsAgentToken())
            throw new AgentScopeForbiddenException("quote creation");
        var v = await validator.ValidateAsync(request, ct);
        if (!v.IsValid) throw new FluentValidation.ValidationException(v.Errors);
        var quote = await quotes.CreateQuoteAsync(User.GetUserId(), request, QuoteOrigin.Human, ct);
        return StatusCode(StatusCodes.Status201Created, quote);
    }

    /// <summary>B5+ contract #2: the caller's active Open Quote (204 when none).</summary>
    [HttpGet("quote")]
    public async Task<ActionResult<QuoteResult>> GetOpenQuote(CancellationToken ct)
    {
        if (User.IsAgentToken())
            throw new AgentScopeForbiddenException("quote discovery");
        var quote = await quotes.GetOpenQuoteAsync(User.GetUserId(), ct);
        return quote is null ? NoContent() : Ok(quote);
    }
}
