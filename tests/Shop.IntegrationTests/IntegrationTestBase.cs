using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Shop.IntegrationTests;

/// <summary>
/// Shared HTTP helpers for a test class fixed to one <see cref="ShopWebFactory"/>
/// subtype — declaring the fixture here (rather than on a concrete base) keeps
/// xunit creating exactly ONE factory per class, even for specialized factories.
/// </summary>
public abstract class IntegrationTestBase<TFactory>(TFactory factory) : IClassFixture<TFactory>
    where TFactory : ShopWebFactory
{
    protected readonly HttpClient Client = factory.CreateClient();
    protected TFactory Factory { get; } = factory;

    protected async Task<string> RegisterAndLoginAsync(string email, string password = "Test1234Aa", bool admin = false)
    {
        // Try login first (seeded users), else register
        var loginRes = await Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        if (loginRes.IsSuccessStatusCode)
        {
            var loginDoc = await loginRes.Content.ReadFromJsonAsync<AuthPayload>();
            return loginDoc!.Token;
        }
        var regRes = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password,
            firstName = "Test",
            lastName = "User",
            phoneNumber = (string?)null
        });
        regRes.EnsureSuccessStatusCode();
        var doc = await regRes.Content.ReadFromJsonAsync<AuthPayload>();
        return doc!.Token;
    }

    protected void UseToken(string token)
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    protected void ClearToken() => Client.DefaultRequestHeaders.Authorization = null;

    protected record AuthPayload(string Token, object User);
    protected record IdPayload(Guid Id);
}

public abstract class IntegrationTestBase(ShopWebFactory factory) : IntegrationTestBase<ShopWebFactory>(factory);
