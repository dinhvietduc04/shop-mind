using System.Net;
using System.Net.Http.Json;

namespace Shop.IntegrationTests;

public class AuthFlowTests(ShopWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Register_Login_Me_Works()
    {
        var email = $"user_{Guid.NewGuid():N}@test.local";
        var reg = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Strong123Aa",
            firstName = "John",
            lastName = "Doe",
            phoneNumber = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);

        var login = await Client.PostAsJsonAsync("/api/auth/login", new { email, password = "Strong123Aa" });
        Assert.True(login.IsSuccessStatusCode);
        var payload = await login.Content.ReadFromJsonAsync<AuthPayload>();
        Assert.NotNull(payload);

        UseToken(payload!.Token);
        var me = await Client.GetAsync("/api/auth/me");
        Assert.True(me.IsSuccessStatusCode);
        ClearToken();
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var email = $"dup_{Guid.NewGuid():N}@test.local";
        var body = new { email, password = "Strong123Aa", firstName = "A", lastName = "B", phoneNumber = (string?)null };
        var first = await Client.PostAsJsonAsync("/api/auth/register", body);
        Assert.True(first.IsSuccessStatusCode);
        var second = await Client.PostAsJsonAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        ClearToken();
        var res = await Client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
