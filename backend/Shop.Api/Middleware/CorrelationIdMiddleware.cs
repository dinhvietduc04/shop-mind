using System.Text.RegularExpressions;
using Shop.Application.Common;

namespace Shop.Api.Middleware;

/// <summary>
/// M10: per-request correlation id. Reuses a well-formed incoming
/// <c>X-Correlation-Id</c>, otherwise generates one; echoes it on the response,
/// scopes it into all request logging, and exposes it to
/// <see cref="CorrelationContext"/> so outbox payloads (and later MCP tool
/// traces) carry the same id across layers. Registered first so even the
/// exception middleware's logs are attributable.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> log)
{
    public const string HeaderName = "X-Correlation-Id";

    // Printable, bounded tokens only — refuse header-injection shapes.
    private static readonly Regex Acceptable = new(@"^[A-Za-z0-9\-_]{1,64}$", RegexOptions.Compiled);

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = Acceptable.IsMatch(incoming)
            ? incoming
            : Guid.NewGuid().ToString("N");

        context.Response.Headers[HeaderName] = correlationId;

        using (CorrelationContext.Begin(correlationId))
        using (log.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}
