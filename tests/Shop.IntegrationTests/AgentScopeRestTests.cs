using System.Net;
using System.Text.Json;

namespace Shop.IntegrationTests;

/// <summary>
/// A1 / ADR-0006: a delegated scope=agent token authenticates but is forbidden
/// on every /api route. Normal human tokens keep working.
/// </summary>
public class AgentScopeRestTests(ShopWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Agent_scope_token_is_forbidden_on_every_api_route()
    {
        UseToken(TestAgentTokens.AgentToken(Guid.NewGuid()));

        var products = await Client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.Forbidden, products.StatusCode);
        using (var doc = JsonDocument.Parse(await products.Content.ReadAsStringAsync()))
            Assert.Equal("AgentScopeForbidden", doc.RootElement.GetProperty("code").GetString());

        var cart = await Client.GetAsync("/api/cart");
        Assert.Equal(HttpStatusCode.Forbidden, cart.StatusCode);
    }

    [Fact]
    public async Task Normal_user_token_still_reaches_api_routes()
    {
        UseToken(await RegisterAndLoginAsync($"agent-scope-{Guid.NewGuid():N}@test.local"));

        var products = await Client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
    }
}
