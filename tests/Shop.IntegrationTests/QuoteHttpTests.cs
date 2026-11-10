using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Shop.Application.Dtos;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Infrastructure.Persistence;

namespace Shop.IntegrationTests;

/// <summary>
/// B5 at the HTTP edge: POST /api/checkout/quote carries the full grill-locked
/// contract — [RequireIdempotency] (B2's deferred exit criterion: replay ×3 →
/// ONE quote), and the 6/min checkout rate bucket covers /quote (B3).
/// Agent-scope JWTs are refused at the auth edge (ADR-0006).
/// </summary>
public class QuoteHttpTests(ShopWebFactory factory) : IntegrationTestBase<ShopWebFactory>(factory)
{
    private static readonly CreateQuoteRequest Request = new(new ShippingAddressDto("Http Quote", "+1-555", "1 Ln", "Town"));

    private AppDbContext NewDb() => factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    private async Task<(string email, string token, Guid userId)> NewUserAsync()
    {
        var email = $"qhttp_{Guid.NewGuid():N}@test.local";
        var token = await RegisterAndLoginAsync(email);
        UseToken(token);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);

        // give the user a cart line, or the quote 400s with EmptyCart
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Status == ProductStatus.Active);
        await ResolveCarts().AddItemAsync(user.Id, new AddCartItemRequest(product.Id, 1));
        return (email, token, user.Id);
    }

    private Shop.Application.Interfaces.ICartService ResolveCarts() =>
        factory.Services.CreateScope().ServiceProvider.GetRequiredService<Shop.Application.Interfaces.ICartService>();

    private async Task<HttpResponseMessage> QuoteAsync(Guid? key)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/quote")
        {
            Content = JsonContent.Create(Request),
        };
        if (key is not null) message.Headers.Add("Idempotency-Key", key.ToString());
        return await Client.SendAsync(message);
    }

    /// <summary>Mints a JWT exactly like the app but with scope=agent — the shape A1/A2 tokens will have.</summary>
    private static string CreateAgentScopedToken(Guid userId, string email)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("shopmind-integration-test-secret-32chars!!"));
        var token = new JwtSecurityToken(
            issuer: "ShopMind", audience: "ShopMind",
            claims: [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Name, email),
                     new Claim(ClaimTypes.Role, nameof(UserRole.Customer)), new Claim("scope", "agent")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task QuoteReplay_ThreeIdenticalRequests_CreateOneQuote()
    {
        // B2's deferred exit criterion, finally on the real money path.
        var (_, _, userId) = await NewUserAsync();
        var key = Guid.NewGuid();

        var first = await QuoteAsync(key);
        var second = await QuoteAsync(key);
        var third = await QuoteAsync(key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, third.StatusCode);
        var body = await first.Content.ReadAsStringAsync();
        Assert.Equal(body, await second.Content.ReadAsStringAsync());
        Assert.Equal(body, await third.Content.ReadAsStringAsync());
        Assert.False(first.Headers.Contains("Idempotent-Replay"));
        Assert.True(second.Headers.Contains("Idempotent-Replay"));

        var db = NewDb();
        Assert.Equal(1, await db.Orders.AsNoTracking().CountAsync(o => o.UserId == userId));
        Assert.Equal(1, await db.PaymentSessions.AsNoTracking().CountAsync(s => s.UserId == userId));
        Assert.Equal(1, await db.OutboxMessages.AsNoTracking().CountAsync(m => m.Type == "order.created"
            && db.Orders.Any(o => o.UserId == userId && o.Id == m.AggregateId)));
    }

    [Fact]
    public async Task DifferentKey_NewQuote_ReplacesOld()
    {
        var (_, _, userId) = await NewUserAsync();

        Assert.Equal(HttpStatusCode.Created, (await QuoteAsync(Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await QuoteAsync(Guid.NewGuid())).StatusCode);

        var db = NewDb();
        Assert.Equal(2, await db.Orders.AsNoTracking().CountAsync(o => o.UserId == userId));
        Assert.Equal(1, await db.Orders.AsNoTracking().CountAsync(o => o.UserId == userId
            && o.Status == OrderStatus.Pending && o.ExpiresAt != null)); // one Open Quote, one replaced
    }

    [Fact]
    public async Task MissingIdempotencyKey_BadRequest_QuoteNotCreated()
    {
        var (_, _, userId) = await NewUserAsync();

        var res = await QuoteAsync(key: null);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(0, await NewDb().Orders.AsNoTracking().CountAsync(o => o.UserId == userId));
    }

    [Fact]
    public async Task AgentScopedToken_Refused403_OnBothQuoteRoutes()
    {
        // ADR-0006: agent tokens never reach money REST — auth-level reject.
        var (email, _, userId) = await NewUserAsync();

        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAgentScopedToken(userId, email));

        using var post = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/quote")
        {
            Content = JsonContent.Create(Request),
        };
        post.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var postRes = await Client.SendAsync(post);
        var getRes = await Client.GetAsync("/api/checkout/quote");

        Assert.Equal(HttpStatusCode.Forbidden, postRes.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, getRes.StatusCode);
        Assert.Equal(0, await NewDb().Orders.AsNoTracking().CountAsync(o => o.UserId == userId));
    }

    [Fact]
    public async Task GetOpenQuote_EmptyThenCurrent()
    {
        var (_, _, userId) = await NewUserAsync();

        var empty = await Client.GetAsync("/api/checkout/quote");
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);

        await QuoteAsync(Guid.NewGuid());
        var res = await Client.GetAsync("/api/checkout/quote");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var quote = await res.Content.ReadFromJsonAsync<QuoteResult>();
        Assert.Single(quote!.Order.Items);
        Assert.StartsWith("/pay/", quote.HostedPayUrl);
        Assert.True(quote.ExpiresAt > DateTime.UtcNow.AddMinutes(25));
    }
}

/// <summary>Checkout/quote bucket = 6/min per plan table; overridden small here.</summary>
public sealed class QuoteRateWebFactory : ShopWebFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimit:CheckoutPerMinute", "2");
    }
}

public class QuoteRateTests(QuoteRateWebFactory factory) : IntegrationTestBase<QuoteRateWebFactory>(factory)
{
    [Fact]
    public async Task ThirdQuoteInWindow_429_WithRetryAfter()
    {
        var email = $"qrate_{Guid.NewGuid():N}@test.local";
        var token = await RegisterAndLoginAsync(email);
        UseToken(token);
        var db = factory.Services.CreateScope().ServiceProvider.GetRequiredService<IAppDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Status == ProductStatus.Active);
        await factory.Services.CreateScope().ServiceProvider
            .GetRequiredService<Shop.Application.Interfaces.ICartService>()
            .AddItemAsync(user.Id, new AddCartItemRequest(product.Id, 1));

        foreach (var _ in Enumerable.Range(0, 2))
        {
            using var ok = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/quote")
            {
                Content = JsonContent.Create(new CreateQuoteRequest(new ShippingAddressDto("R", "1", "a", "c"))),
            };
            ok.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            var res = await Client.SendAsync(ok);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        }

        using var third = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/quote")
        {
            Content = JsonContent.Create(new CreateQuoteRequest(new ShippingAddressDto("R", "1", "a", "c"))),
        };
        third.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var blocked = await Client.SendAsync(third);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));
    }
}
