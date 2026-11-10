namespace Shop.Api.Middleware;

/// <summary>
/// B5 (ADR-0002): request-line logging with a hard exclusion — pay URLs are
/// capability credentials, so /pay/* paths (tokens included) are NEVER logged.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var isPayPath = path.StartsWith("/pay", StringComparison.OrdinalIgnoreCase);

        await next(context);

        if (!isPayPath)
            log.LogInformation("HTTP {Method} {Path} -> {Status}",
                context.Request.Method, path, context.Response.StatusCode);
        else
            log.LogInformation("HTTP {Method} /pay/* -> {Status}", context.Request.Method, context.Response.StatusCode);
    }
}
