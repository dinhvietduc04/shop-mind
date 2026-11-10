using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Shop.Application.Interfaces;
using Shop.Domain.Exceptions;
using Shop.Infrastructure.Idempotency;

namespace Shop.Api.Idempotency;

/// <summary>
/// B2 (ADR-0004): enforces <c>Idempotency-Key</c> (UUIDv4) on endpoints marked
/// <see cref="RequireIdempotencyAttribute"/>. Keyed per user + route + client key:
///   • fresh key            → lease (renewed while the handler runs), execute, store response
///   • same key, same body  → replay stored response with <c>Idempotent-Replay: true</c>
///   • same key, new body   → 409 Conflict
///   • same key, in flight  → 409 + Retry-After
///   • store unreachable    → 503 + Retry-After, handler NEVER runs (fail-closed)
/// A handler that crashes or 5xxes releases the lease so honest retries can run.
/// </summary>
public sealed class IdempotencyMiddleware(
    RequestDelegate next,
    IIdempotencyStore store,
    IOptions<IdempotencyOptions> options,
    ILogger<IdempotencyMiddleware> log)
{
    private static readonly Regex UuidV4 = new(
        "^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IdempotencyOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<RequireIdempotencyAttribute>() is null)
        {
            await next(context);
            return;
        }

        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
        if (!UuidV4.IsMatch(idempotencyKey))
        {
            await WriteAsync(context, 400, "InvalidIdempotencyKey",
                "Idempotency-Key header is required and must be a UUIDv4.");
            return;
        }

        string requestHash;
        try
        {
            requestHash = await HashBodyAsync(context.Request);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await WriteAsync(context, 400, "UnreadableRequest", "Request body could not be read.");
            return;
        }

        var key = BuildKey(context, idempotencyKey);

        IdempotencyAcquisition acquisition;
        try
        {
            acquisition = await store.TryAcquireAsync(key, requestHash, context.RequestAborted);
        }
        catch (IdempotencyUnavailableException ex)
        {
            // ADR-0004: fail CLOSED on money paths — never run un-deduplicated.
            log.LogWarning("Idempotency store unavailable for {Key}: {Reason}", key, ex.Message);
            context.Response.Headers["Retry-After"] = "5";
            await WriteAsync(context, 503, "IdempotencyUnavailable", ex.Message);
            return;
        }

        switch (acquisition.Result)
        {
            case IdempotencyAcquisitionResult.Replay:
                await ReplayAsync(context, acquisition.StoredResponse!);
                return;

            case IdempotencyAcquisitionResult.PayloadMismatch:
                await WriteAsync(context, 409, "IdempotencyPayloadMismatch",
                    "Idempotency-Key was already used with a different request payload.");
                return;

            case IdempotencyAcquisitionResult.InProgress:
                context.Response.Headers["Retry-After"] = acquisition.RetryAfterSeconds!.Value.ToString();
                await WriteAsync(context, 409, "IdempotencyInProgress",
                    "A request with this Idempotency-Key is still in progress.");
                return;

            case IdempotencyAcquisitionResult.Acquired:
                await ExecuteWithLeaseAsync(context, key, acquisition.LeaseToken!);
                return;
        }
    }

    private async Task ExecuteWithLeaseAsync(HttpContext context, string key, string leaseToken)
    {
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        using var renewalCts = new CancellationTokenSource();
        var renewalTask = RenewLoopAsync(key, leaseToken, renewalCts.Token);

        try
        {
            await next(context);
        }
        catch
        {
            context.Response.Body = originalBody;
            renewalCts.Cancel();
            await ForgetAsync(renewalTask);
            await TryReleaseAsync(key, leaseToken); // crashed handler: free the key for an honest retry
            throw; // ExceptionHandlingMiddleware turns this into the 500
        }

        context.Response.Body = originalBody;
        renewalCts.Cancel();
        await ForgetAsync(renewalTask);

        buffer.Position = 0;
        var body = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync(context.RequestAborted);

        if (context.Response.StatusCode >= 500)
        {
            // Server errors are not replayable outcomes: release so a retry re-executes.
            await TryReleaseAsync(key, leaseToken);
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
            return;
        }

        var stored = new StoredIdempotentResponse(
            context.Response.StatusCode, context.Response.ContentType ?? "application/json", body);

        bool won;
        try
        {
            won = await store.CompleteAsync(key, leaseToken, stored, context.RequestAborted);
        }
        catch (IdempotencyUnavailableException)
        {
            won = false; // can't record; the response below is still the honest first outcome
        }

        if (!won)
            log.LogWarning("Idempotency lease for {Key} was taken over mid-request; serving this completed "
                + "response without recording it (DB constraints are the backstop)", key);

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, context.RequestAborted);
    }

    /// <summary>ADR-0004: holder renews ~lease/3 so a slow (but alive) request is never stolen from.</summary>
    private async Task RenewLoopAsync(string key, string leaseToken, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.RenewSeconds)));
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!await store.RenewLeaseAsync(key, leaseToken, ct))
                {
                    log.LogWarning("Idempotency lease for {Key} no longer ours; stopping renewal", key);
                    return;
                }
            }
        }
        catch (OperationCanceledException) { /* request finished */ }
        catch (Exception ex)
        {
            // Renewal failure cannot break a request that may still complete fine;
            // the lease simply expires and the owner-checked CAS guards the write.
            log.LogWarning(ex, "Idempotency renewal loop faulted for {Key}", key);
        }
    }

    private static async Task<string> HashBodyAsync(HttpRequest request)
    {
        request.EnableBuffering(); // money POST bodies are small; keep it re-readable
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(request.Body, request.HttpContext.RequestAborted);
        request.Body.Position = 0;
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildKey(HttpContext context, string idempotencyKey)
    {
        // Plan shape: shop:idem:{userId}:{routeHash}:{key} — the store prepends KeyPrefix.
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub")
            ?? "anon";
        var route = $"{context.Request.Method}:{context.Request.Path.Value}";
        var routeHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(route)))[..12].ToLowerInvariant();
        return $"{userId}:{routeHash}:{idempotencyKey}";
    }

    private async Task TryReleaseAsync(string key, string leaseToken)
    {
        try { await store.ReleaseAsync(key, leaseToken, CancellationToken.None); }
        catch (Exception ex) { log.LogWarning(ex, "Idempotency release faulted for {Key}; lease will expire", key); }
    }

    private static async Task ForgetAsync(Task task)
    {
        try { await task; }
        catch { /* renewal is advisory */ }
    }

    private static async Task ReplayAsync(HttpContext context, StoredIdempotentResponse stored)
    {
        context.Response.Headers["Idempotent-Replay"] = "true";
        context.Response.StatusCode = stored.StatusCode;
        context.Response.ContentType = stored.ContentType;
        await context.Response.WriteAsync(stored.Body, context.RequestAborted);
    }

    private static Task WriteAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = status;
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { title = message, status, code }));
    }
}
