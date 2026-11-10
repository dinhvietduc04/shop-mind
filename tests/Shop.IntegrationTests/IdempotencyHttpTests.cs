using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.IntegrationTests;

/// <summary>
/// B2 at the HTTP edge: Idempotency-Key semantics through the full middleware
/// stack (plan §B2 exit criteria, mounted on the test probe — B5 applies the
/// same attribute to /api/checkout/quote).
/// Seam: [RequireIdempotency] endpoints via ShopWebFactory.
/// </summary>
public class IdempotencyHttpTests(ShopWebFactory factory) : IntegrationTestBase(factory)
{
    private const string Route = "/api/__probe/idempotency";
    private static readonly Guid Key = Guid.NewGuid(); // stable "client-generated" key for replay tests

    private ProbeCounter Counter() => factory.Services.GetRequiredService<ProbeCounter>();

    private async Task<(HttpResponseMessage Res, string Body)> PostAsync(Guid? key, ProbeRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key.ToString());
        var res = await Client.SendAsync(request);
        return (res, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MissingKeyHeader_Rejected400_HandlerNeverRuns()
    {
        var before = Counter().Count;

        var (res, _) = await PostAsync(key: null, new ProbeRequest("x", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(before, Counter().Count);
    }

    [Fact]
    public async Task NonUuidV4Key_Rejected400()
    {
        // A v1 UUID is a well-formed GUID but not the agreed wire format.
        var before = Counter().Count;

        var (res, _) = await PostAsync(new Guid("59d7e4e0-b2c3-11ef-9b3c-0242ac120002"), new ProbeRequest("x", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var (notGuid, _) = await PostAsync(default(Guid), new ProbeRequest("x", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, notGuid.StatusCode);
        Assert.Equal(before, Counter().Count);
    }

    [Fact]
    public async Task Replay_ThreeIdenticalRequests_ExecutesOnce_ReturnsThreeIdenticalResponses()
    {
        // Plan §B2 exit criterion: replay ×3 with same key → one execution, identical responses.
        var before = Counter().Count;

        var (first, firstBody) = await PostAsync(Key, new ProbeRequest("quote-me", null, null));
        var (second, secondBody) = await PostAsync(Key, new ProbeRequest("quote-me", null, null));
        var (third, thirdBody) = await PostAsync(Key, new ProbeRequest("quote-me", null, null));

        Assert.Equal(before + 1, Counter().Count);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, third.StatusCode);
        Assert.Equal(firstBody, secondBody);
        Assert.Equal(firstBody, thirdBody);

        Assert.False(first.Headers.TryGetValues("Idempotent-Replay", out _));
        Assert.True(second.Headers.Contains("Idempotent-Replay"));
        Assert.True(third.Headers.Contains("Idempotent-Replay"));
    }

    [Fact]
    public async Task DifferentKey_SameUser_IsANewExecution()
    {
        var before = Counter().Count;

        var (first, _) = await PostAsync(Guid.NewGuid(), new ProbeRequest("a", null, null));
        var (second, _) = await PostAsync(Guid.NewGuid(), new ProbeRequest("a", null, null));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(before + 2, Counter().Count);
    }

    [Fact]
    public async Task SameKey_DifferentPayload_Conflicts409_AndOriginalStillReplays()
    {
        // Plan §B2: same key, different payload → 409 Conflict.
        var key = Guid.NewGuid();
        var (first, firstBody) = await PostAsync(key, new ProbeRequest("original", null, null));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var before = Counter().Count;
        var (conflict, _) = await PostAsync(key, new ProbeRequest("DIFFERENT payload", null, null));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(before, Counter().Count); // mismatch never re-executes

        var (replay, replayBody) = await PostAsync(key, new ProbeRequest("original", null, null));
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(firstBody, replayBody);
    }

    [Fact]
    public async Task SameKey_WhileStillInProgress_Conflicts409WithRetryAfter()
    {
        // Plan §B2: still in progress → 409 + Retry-After. A slow handler plus a
        // parallel duplicate makes the window deterministic.
        var key = Guid.NewGuid();

        var slow = Task.Run(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Route)
            {
                Content = JsonContent.Create(new ProbeRequest("slow", 2500, null)),
            };
            request.Headers.Add("Idempotency-Key", key.ToString());
            return await Client.SendAsync(request);
        });
        await Task.Delay(600); // let the first request take the lease

        var (dup, _) = await PostAsync(key, new ProbeRequest("slow", null, null));

        var slowRes = await slow;

        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.True(dup.Headers.Contains("Retry-After"));
        Assert.Equal(HttpStatusCode.Created, slowRes.StatusCode);
    }

    [Fact]
    public async Task HandlerCrash_ReleasesLease_SameKeyCanRetryAndExecute()
    {
        // A crashed attempt must not wedge the key: it releases its lease so the
        // honest retry re-executes (DB unique constraints stay the final backstop).
        var key = Guid.NewGuid();
        var (crashed, _) = await PostAsync(key, new ProbeRequest("boom", null, true));
        Assert.Equal(HttpStatusCode.InternalServerError, crashed.StatusCode);

        var (retry, _) = await PostAsync(key, new ProbeRequest("boom-fixed", null, null));
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    [Fact]
    public async Task ErroredAttempt_LeaseReleased_RetrySucceeds()
    {
        // Same key, second attempt differs from the (never-completed) crashed payload:
        // release means no mismatch ghost either.
        var key = Guid.NewGuid();
        await PostAsync(key, new ProbeRequest("boom", null, true));
        var (retry, body) = await PostAsync(key, new ProbeRequest("boom", null, null));
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Contains("Execution", body.Replace("execution", "Execution"));
    }
}

/// <summary>
/// B2 / ADR-0004 fail-closed: with the idempotency store unreachable, money POSTs
/// must refuse (503 + Retry-After) and the handler must NOT run.
/// </summary>
public sealed class IdempotencyFailClosedTests : IClassFixture<DeadRedisWebFactory>
{
    private readonly DeadRedisWebFactory _factory;

    public IdempotencyFailClosedTests(DeadRedisWebFactory factory) => _factory = factory;

    [Fact]
    public async Task StoreUnreachable_Refuses503WithRetryAfter_HandlerNeverRuns()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/__probe/idempotency")
        {
            Content = JsonContent.Create(new ProbeRequest("x", null, null)),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var res = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.True(res.Headers.Contains("Retry-After"));
        Assert.Equal(0, _factory.Services.GetRequiredService<ProbeCounter>().Count);
    }
}
