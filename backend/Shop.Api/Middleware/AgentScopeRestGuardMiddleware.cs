using Shop.Api.Extensions;
using Shop.Domain.Exceptions;

namespace Shop.Api.Middleware;

/// <summary>
/// A1 / ADR-0006: delegated scope=agent JWTs are for Shop.Mcp only. They may
/// read the anonymous hosted pay page, but every /api REST route is forbidden
/// at the auth edge before controllers, rate limiting, or idempotency run.
/// </summary>
public sealed class AgentScopeRestGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api") && context.User.IsAgentToken())
        {
            var error = new AgentScopeForbiddenException("REST endpoints");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                title = error.Message,
                status = StatusCodes.Status403Forbidden,
                code = error.Code,
            });
            return;
        }

        await next(context);
    }
}
