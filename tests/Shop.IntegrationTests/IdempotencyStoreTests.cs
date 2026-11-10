using Shop.Application.Interfaces;
using Shop.Infrastructure.Idempotency;

namespace Shop.IntegrationTests;

/// <summary>
/// B2 (ADR-0004): the idempotency record is a renewing Redis lease — SET NX 30s,
/// holder-only compare-and-renew, owner-checked CAS to Done, abandoned leases
/// expire and become re-acquirable. These tests pin the store semantics directly
/// against a live Redis (namespaced keys, never FLUSHALL).
/// Seam: IIdempotencyStore.
/// </summary>
public sealed class IdempotencyStoreTests : IAsyncLifetime
{
    private readonly TestRedis _redis = new();

    public Task InitializeAsync() => _redis.InitializeAsync();
    public Task DisposeAsync() => _redis.DisposeAsync();

    private RedisIdempotencyStore NewStore(int leaseSeconds = 30) => new(new IdempotencyOptions
    {
        ConnectionString = _redis.ConnectionString,
        KeyPrefix = _redis.KeyPrefix,
        LeaseSeconds = leaseSeconds,
        RenewSeconds = Math.Max(1, leaseSeconds / 3),
    });

    [Fact]
    public async Task FreshKey_TryAcquire_GrantsLeaseWithHolderToken()
    {
        var store = NewStore();

        var first = await store.TryAcquireAsync("user1/quote/abc", "hash-a");

        Assert.Equal(IdempotencyAcquisitionResult.Acquired, first.Result);
        Assert.False(string.IsNullOrEmpty(first.LeaseToken));

        var record = await store.GetAsync("user1/quote/abc");
        Assert.NotNull(record);
        Assert.Equal(IdempotencyState.InProgress, record.State);
        Assert.Equal("hash-a", record.RequestHash);
        Assert.Equal(first.LeaseToken, record.LeaseHolderToken);
    }

    [Fact]
    public async Task SameKey_WhileLeaseHeld_SecondAcquirerGetsInProgressWithRetryAfter()
    {
        var store = NewStore();
        await store.TryAcquireAsync("user1/quote/abc", "hash-a");

        var second = await store.TryAcquireAsync("user1/quote/abc", "hash-a");

        Assert.Equal(IdempotencyAcquisitionResult.InProgress, second.Result);
        Assert.Null(second.LeaseToken);
        Assert.NotNull(second.RetryAfterSeconds);
        Assert.InRange(second.RetryAfterSeconds!.Value, 1, 30);
    }

    [Fact]
    public async Task DifferentKeys_Independent_AreAcquirableTogether()
    {
        var store = NewStore();

        var a = await store.TryAcquireAsync("user1/quote/aaa", "hash-a");
        var b = await store.TryAcquireAsync("user1/quote/bbb", "hash-b");

        Assert.Equal(IdempotencyAcquisitionResult.Acquired, a.Result);
        Assert.Equal(IdempotencyAcquisitionResult.Acquired, b.Result);
        Assert.NotEqual(a.LeaseToken, b.LeaseToken);
    }

    [Fact]
    public async Task Renew_OnlyTheHolder_CanExtendTheLease()
    {
        var store = NewStore(leaseSeconds: 4);
        var acquired = await store.TryAcquireAsync("user1/quote/renew", "hash-a");
        var token = acquired.LeaseToken!;

        var stranger = await store.RenewLeaseAsync("user1/quote/renew", "not-my-lease");
        Assert.False(stranger);

        // Two lapses of a 4s lease with renewal in between each time: the lease is
        // only still alive because the holder extended it. (Delays use ~60% of the
        // lease so suite load cannot starve the margin.)
        for (var lap = 0; lap < 2; lap++)
        {
            await Task.Delay(2500);
            Assert.True(await store.RenewLeaseAsync("user1/quote/renew", token));
        }

        await Task.Delay(2500); // would have expired without the last renewal
        var record = await store.GetAsync("user1/quote/renew");
        Assert.NotNull(record);
        Assert.Equal(token, record.LeaseHolderToken);
    }

    [Fact]
    public async Task ExpiredAbandonedLease_BecomesReAcquirable()
    {
        // ADR-0004: an abandoned lease simply expires; the key is re-acquirable.
        var store = NewStore(leaseSeconds: 1);
        await store.TryAcquireAsync("user1/quote/drop", "hash-a");

        await Task.Delay(1400);
        Assert.Null(await store.GetAsync("user1/quote/drop"));

        var reacquired = await store.TryAcquireAsync("user1/quote/drop", "hash-a");
        Assert.Equal(IdempotencyAcquisitionResult.Acquired, reacquired.Result);
    }

    [Fact]
    public async Task Complete_IsOwnerChecked_AndTransitionsToDone()
    {
        var store = NewStore();
        var acquired = await store.TryAcquireAsync("user1/quote/done", "hash-a");

        var stolen = await store.CompleteAsync(
            "user1/quote/done", "wrong-token", new StoredIdempotentResponse(201, "application/json", "{}"));
        Assert.False(stolen); // zombie holders cannot write Done

        var response = new StoredIdempotentResponse(201, "application/json", "{\"orderId\":\"x\"}");
        Assert.True(await store.CompleteAsync("user1/quote/done", acquired.LeaseToken!, response));

        var record = await store.GetAsync("user1/quote/done");
        Assert.NotNull(record);
        Assert.Equal(IdempotencyState.Done, record.State);
        Assert.Equal(response, record.Response);
    }

    [Fact]
    public async Task DoneKey_SameHash_ReplaysStoredResponse_DifferentHash_Conflicts()
    {
        var store = NewStore();
        var acquired = await store.TryAcquireAsync("user1/quote/rply", "hash-a");
        var response = new StoredIdempotentResponse(201, "application/json", "{\"n\":1}");
        Assert.True(await store.CompleteAsync("user1/quote/rply", acquired.LeaseToken!, response));

        var replay = await store.TryAcquireAsync("user1/quote/rply", "hash-a");
        Assert.Equal(IdempotencyAcquisitionResult.Replay, replay.Result);
        Assert.Equal(response, replay.StoredResponse);

        var mismatch = await store.TryAcquireAsync("user1/quote/rply", "hash-different-payload");
        Assert.Equal(IdempotencyAcquisitionResult.PayloadMismatch, mismatch.Result);
    }

    [Fact]
    public async Task StolenLease_ZombieComplete_CannotOverwriteTakeoverResult()
    {
        // The ADR-0004 heart: A's lease lapses, B takes over and completes;
        // A waking up late must not clobber B's Done record.
        var store = NewStore(leaseSeconds: 1);
        var a = await store.TryAcquireAsync("user1/quote/steal", "hash-a");

        await Task.Delay(1400);
        var b = await store.TryAcquireAsync("user1/quote/steal", "hash-a");
        Assert.Equal(IdempotencyAcquisitionResult.Acquired, b.Result);

        var bResponse = new StoredIdempotentResponse(201, "application/json", "{\"by\":\"B\"}");
        Assert.True(await store.CompleteAsync("user1/quote/steal", b.LeaseToken!, bResponse));

        var zombieResponse = new StoredIdempotentResponse(201, "application/json", "{\"by\":\"A\"}");
        Assert.False(await store.CompleteAsync("user1/quote/steal", a.LeaseToken!, zombieResponse));

        var replay = await store.TryAcquireAsync("user1/quote/steal", "hash-a");
        Assert.Equal("{\"by\":\"B\"}", replay.StoredResponse!.Body);
    }

    [Fact]
    public async Task Release_IsOwnerChecked_AndFreesTheKeyImmediately()
    {
        // Errored attempts release so the same key can retry without waiting out the lease.
        var store = NewStore();
        var acquired = await store.TryAcquireAsync("user1/quote/rel", "hash-a");

        Assert.False(await store.ReleaseAsync("user1/quote/rel", "not-my-lease"));

        Assert.True(await store.ReleaseAsync("user1/quote/rel", acquired.LeaseToken!));
        Assert.Null(await store.GetAsync("user1/quote/rel"));

        var retry = await store.TryAcquireAsync("user1/quote/rel", "hash-a");
        Assert.Equal(IdempotencyAcquisitionResult.Acquired, retry.Result);
    }

    [Fact]
    public async Task UnreachableStore_FailsClosed_ThrowsIdempotencyUnavailable()
    {
        // ADR-0004 fail-closed: when the store cannot answer, callers get an
        // explicit refusal — never a "probably fine" pass-through.
        using var dead = new RedisIdempotencyStore(new IdempotencyOptions
        {
            ConnectionString = "127.0.0.1:1,connectTimeout=200,connectRetry=1,abortConnect=true",
            KeyPrefix = "shopmindtest:dead:",
        });

        var act = async () => await dead.TryAcquireAsync("user1/quote/x", "hash-a");
        await Assert.ThrowsAsync<Shop.Domain.Exceptions.IdempotencyUnavailableException>(act);
    }
}
