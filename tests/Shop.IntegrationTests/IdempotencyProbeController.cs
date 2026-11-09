using Microsoft.AspNetCore.Mvc;
using Shop.Api.Idempotency;

namespace Shop.IntegrationTests;

/// <summary>
/// B2 test seam: the money-POST shape the idempotency middleware guards, without
/// pulling B5's quote endpoint forward. The counter proves execute-once semantics
/// end-to-end through the full middleware stack.
/// </summary>
[ApiController]
[Route("api/__probe/idempotency")]
public class IdempotencyProbeController(ProbeCounter counter) : ControllerBase
{
    [HttpPost]
    [RequireIdempotency]
    public async Task<IActionResult> Probe([FromBody] ProbeRequest? request, CancellationToken ct)
    {
        if (request?.DelayMs is > 0)
            await Task.Delay(request.DelayMs.Value, ct);

        var execution = counter.Record();

        if (request?.Boom == true)
            throw new InvalidOperationException("probe handler crashed");

        return StatusCode(201, new ProbeResult(execution, request?.Echo));
    }
}

public sealed record ProbeRequest(string? Echo, int? DelayMs, bool? Boom);
public sealed record ProbeResult(int Execution, string? Echo);

public sealed class ProbeCounter
{
    private int _count;
    public int Record() => Interlocked.Increment(ref _count);
    public int Count => Volatile.Read(ref _count);
}
