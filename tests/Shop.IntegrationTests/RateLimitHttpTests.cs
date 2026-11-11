using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.IntegrationTests;

/// <summary>
/// B3-L1 at the HTTP edge: the plan's Layer-1 bucket table enforced through the
/// full middleware stack (429 + Retry-After + X-RateLimit-* headers), per-bucket
/// partition isolation, and fail-closed 503 when Redis cannot answer.
/// Seam: real REST routes via ShopWebFactory. The base factory defaults every
/// bucket effectively-unlimited; each subclass opens only its own bucket small.
/// </summary>
public abstract class RateLimitWebFactory : ShopWebFactory
{
    /// <summary>A route the middleware still counts but everything else ignores.</summary>
    internal const string AnyRoute = "/api/__any-request";
}

public sealed class GlobalBucketWebFactory : RateLimitWebFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimit:GlobalPerMinute", "5");
    }
}

public sealed class AuthBucketWebFactory : RateLimitWebFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimit:AuthPerMinute", "3");
    }
}

public sealed class UserBucketsWebFactory : RateLimitWebFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimit:CatalogPerMinute", "4");
        builder.UseSetting("RateLimit:CartWritePerMinute", "3");
        builder.UseSetting("RateLimit:CheckoutPerMinute", "2");
    }
}

/// <summary>A limit of zero disables the bucket instead of denying everything.</summary>
public sealed class DisabledBucketWebFactory : RateLimitWebFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimit:GlobalPerMinute", "0");
    }
}

public class GlobalRateLimitTests(GlobalBucketWebFactory factory) : IntegrationTestBase<GlobalBucketWebFactory>(factory)
{
    [Fact]
    public async Task BurstPastGlobalWindow_429WithRetryAfterAndHeaders_EverythingElseUnlimited()
    {
        // First 5 pass with X-RateLimit-* headers; the 6th is refused, handler never runs.
        for (var i = 0; i < 5; i++)
        {
            var ok = await Client.GetAsync(RateLimitWebFactory.AnyRoute);
            Assert.True(ok.StatusCode is not HttpStatusCode.TooManyRequests and not HttpStatusCode.ServiceUnavailable,
                $"request {i} should not be limited, got {ok.StatusCode}");
            Assert.Equal("5", ok.Headers.GetValues("X-RateLimit-Limit").Single());
            Assert.Equal(4 - i, int.Parse(ok.Headers.GetValues("X-RateLimit-Remaining").Single()));

            // X-RateLimit-Reset is a UTC epoch second (GitHub/Twitter convention)…
            var resetEpoch = long.Parse(ok.Headers.GetValues("X-RateLimit-Reset").Single());
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Assert.InRange(resetEpoch, now, now + 61);

            // …while the IETF family reports the same reset as delta seconds.
            Assert.Equal("5", ok.Headers.GetValues("RateLimit-Limit").Single());
            Assert.Equal(4 - i, int.Parse(ok.Headers.GetValues("RateLimit-Remaining").Single()));
            Assert.True(int.Parse(ok.Headers.GetValues("RateLimit-Reset").Single()) is >= 0 and <= 60);
        }

        var blocked = await Client.GetAsync(RateLimitWebFactory.AnyRoute);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(int.Parse(blocked.Headers.RetryAfter?.ToString() ?? "0") is >= 1 and <= 60);
        var payload = await blocked.Content.ReadFromJsonAsync<ErrorEnvelope>();
        Assert.Equal("RateLimited", payload!.Code);
    }

    private record ErrorEnvelope(string Title, int Status, string Code);
}

public class DisabledBucketTests(DisabledBucketWebFactory factory) : IntegrationTestBase<DisabledBucketWebFactory>(factory)
{
    [Fact]
    public async Task ZeroLimit_DisablesTheBucket_NoRefusalAndNoHeaders()
    {
        // Global is the only bucket an unknown route hits; 0 = off, not "deny all".
        for (var i = 0; i < 10; i++)
        {
            var res = await Client.GetAsync(RateLimitWebFactory.AnyRoute);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, res.StatusCode);
            Assert.NotEqual(HttpStatusCode.ServiceUnavailable, res.StatusCode);
            Assert.False(res.Headers.Contains("X-RateLimit-Limit"));
        }
    }
}

public class AuthRateLimitTests(AuthBucketWebFactory factory) : IntegrationTestBase<AuthBucketWebFactory>(factory)
{
    [Fact]
    public async Task CredentialStuffing_FourthLoginFromSameIp_429_OtherRoutesKeepWorking()
    {
        for (var i = 0; i < 3; i++)
        {
            var attempt = await Client.PostAsJsonAsync("/api/auth/login",
                new { email = "nobody@test.local", password = "wrong" });
            Assert.False(attempt.StatusCode == HttpStatusCode.TooManyRequests);
        }

        var blocked = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@test.local", password = "wrong" });

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));
        Assert.Equal("3", blocked.Headers.GetValues("X-RateLimit-Limit").Single());

        // The auth bucket is its own partition space: catalog (also IP-keyed but
        // different bucket) is untouched by the exhaustion above.
        var catalog = await Client.GetAsync("/api/categories");
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
    }
}

public class UserBucketRateLimitTests(UserBucketsWebFactory factory) : IntegrationTestBase<UserBucketsWebFactory>(factory)
{
    private async Task<string> FreshUserAsync()
    {
        var email = $"rl_{Guid.NewGuid():N}@test.local";
        var token = await RegisterAndLoginAsync(email);
        UseToken(token);
        return email;
    }

    [Fact]
    public async Task Catalog_BurstPastPerUserWindow_429_AndAnotherUserIsUnaffected()
    {
        await FreshUserAsync();

        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/products")).StatusCode);

        var blocked = await Client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal("4", blocked.Headers.GetValues("X-RateLimit-Limit").Single());

        // Per-user partition: a second user gets its own full bucket.
        await FreshUserAsync();
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/products")).StatusCode);
    }

    [Fact]
    public async Task CartWrites_BurstPastWindow_FourthWriteRefused_ButReadsStillFine()
    {
        await FreshUserAsync();

        // Bogus product ids → 404 from the handler, but each write consumed a permit.
        for (var i = 0; i < 3; i++)
        {
            var write = await Client.PostAsJsonAsync("/api/cart/items",
                new { productId = Guid.NewGuid(), quantity = 1 });
            Assert.NotEqual(HttpStatusCode.TooManyRequests, write.StatusCode);
        }

        var blocked = await Client.PostAsJsonAsync("/api/cart/items",
            new { productId = Guid.NewGuid(), quantity = 1 });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal("3", blocked.Headers.GetValues("X-RateLimit-Limit").Single());

        // GET /api/cart is NOT in the write bucket.
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/cart")).StatusCode);
    }

    [Fact]
    public async Task Checkout_BurstPastWindow_ThirdCreateRefused()
    {
        await FreshUserAsync();

        for (var i = 0; i < 2; i++)
        {
            var attempt = await Client.PostAsJsonAsync("/api/checkout", new
            {
                shippingAddress = new { fullName = "T", phone = "1", address = "a", city = "c" },
                paymentMethod = "fake",
            });
            Assert.NotEqual(HttpStatusCode.TooManyRequests, attempt.StatusCode); // empty-cart 400s still count
        }

        var blocked = await Client.PostAsJsonAsync("/api/checkout", new
        {
            shippingAddress = new { fullName = "T", phone = "1", address = "a", city = "c" },
            paymentMethod = "fake",
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal("2", blocked.Headers.GetValues("X-RateLimit-Limit").Single());
    }
}

/// <summary>
/// B3 fail-closed: with Redis unreachable, EVERY request is refused 503 +
/// Retry-After before the handler runs (ADR-0004 — limiter denies, never un-throttled).
/// </summary>
public sealed class RateLimitFailClosedTests : IClassFixture<DeadRedisWebFactory>
{
    private readonly DeadRedisWebFactory _factory;

    public RateLimitFailClosedTests(DeadRedisWebFactory factory) => _factory = factory;

    [Fact]
    public async Task StoreUnreachable_EveryRequestRefused503_HandlerNeverRuns()
    {
        using var client = _factory.CreateClient();

        var res = await client.GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.True(res.Headers.Contains("Retry-After"));
        var payload = await res.Content.ReadFromJsonAsync<ErrorEnvelope>();
        Assert.Equal("RateLimitUnavailable", payload!.Code);
    }

    private record ErrorEnvelope(string Title, int Status, string Code);
}
