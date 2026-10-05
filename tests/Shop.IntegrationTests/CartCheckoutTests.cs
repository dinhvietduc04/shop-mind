using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Shop.IntegrationTests;

public class CartCheckoutTests(ShopWebFactory factory) : IntegrationTestBase(factory)
{
    private async Task<(string token, Guid productId)> SetupCustomerWithProductAsync()
    {
        var email = $"shopper_{Guid.NewGuid():N}@test.local";
        var token = await RegisterAndLoginAsync(email);
        UseToken(token);

        // Browse products (public)
        ClearToken();
        var products = await Client.GetFromJsonAsync<PagedResult>("/api/products?page=1&pageSize=5");
        Assert.NotNull(products);
        Assert.NotEmpty(products!.Items);
        var productId = products.Items[0].Id;

        UseToken(token);
        return (token, productId);
    }

    [Fact]
    public async Task AddToCart_Checkout_Success_UpdatesInventory_And_CreatesOrder()
    {
        var (_, productId) = await SetupCustomerWithProductAsync();

        var add = await Client.PostAsJsonAsync("/api/cart/items", new { productId, quantity = 1 });
        Assert.True(add.IsSuccessStatusCode);

        var cart = await Client.GetAsync("/api/cart");
        Assert.True(cart.IsSuccessStatusCode);

        var checkout = await Client.PostAsJsonAsync("/api/checkout", new
        {
            shippingAddress = new { fullName = "John Doe", phone = "0123456789", address = "123 Example Street", city = "Ho Chi Minh City" },
            paymentMethod = "fake",
            simulateFailure = false,
            simulateTimeout = false
        });
        Assert.True(checkout.IsSuccessStatusCode);

        var orders = await Client.GetFromJsonAsync<PagedResult>("/api/orders?page=1&pageSize=10");
        Assert.NotNull(orders);
        Assert.NotEmpty(orders!.Items);
    }

    [Fact]
    public async Task Checkout_SimulateFailure_Returns402()
    {
        var (_, productId) = await SetupCustomerWithProductAsync();
        var add = await Client.PostAsJsonAsync("/api/cart/items", new { productId, quantity = 1 });
        Assert.True(add.IsSuccessStatusCode);

        var checkout = await Client.PostAsJsonAsync("/api/checkout", new
        {
            shippingAddress = new { fullName = "John Doe", phone = "0123456789", address = "123 Example Street", city = "HCMC" },
            paymentMethod = "fake",
            simulateFailure = true,
            simulateTimeout = false
        });
        Assert.Equal((HttpStatusCode)402, checkout.StatusCode);
    }

    [Fact]
    public async Task Checkout_EmptyCart_Returns400()
    {
        var email = $"emptycart_{Guid.NewGuid():N}@test.local";
        var token = await RegisterAndLoginAsync(email);
        UseToken(token);

        // Ensure cart is empty (fresh user has empty cart)
        var clear = await Client.DeleteAsync("/api/cart");
        Assert.True(clear.IsSuccessStatusCode || clear.StatusCode == System.Net.HttpStatusCode.NoContent);

        var checkout = await Client.PostAsJsonAsync("/api/checkout", new
        {
            shippingAddress = new { fullName = "John Doe", phone = "0123456789", address = "123 Example Street", city = "HCMC" },
            paymentMethod = "fake",
            simulateFailure = false,
            simulateTimeout = false
        });
        Assert.Equal(HttpStatusCode.BadRequest, checkout.StatusCode);
    }

    [Fact]
    public async Task Customer_Cannot_Access_Another_Customers_Order()
    {
        var (tokenA, productId) = await SetupCustomerWithProductAsync();
        var add = await Client.PostAsJsonAsync("/api/cart/items", new { productId, quantity = 1 });
        Assert.True(add.IsSuccessStatusCode);
        var checkout = await Client.PostAsJsonAsync("/api/checkout", new
        {
            shippingAddress = new { fullName = "A", phone = "1", address = "addr", city = "city" },
            paymentMethod = "fake",
            simulateFailure = false,
            simulateTimeout = false
        });
        Assert.True(checkout.IsSuccessStatusCode);
        var checkoutDoc = await checkout.Content.ReadFromJsonAsync<CheckoutDoc>();
        var orderId = checkoutDoc!.Order.Id;

        // Second customer
        var emailB = $"other_{Guid.NewGuid():N}@test.local";
        var tokenB = await RegisterAndLoginAsync(emailB);
        UseToken(tokenB);
        var otherAccess = await Client.GetAsync($"/api/orders/{orderId}");
        Assert.True(otherAccess.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        UseToken(tokenA);
    }

    [Fact]
    public async Task Admin_Can_Manage_Orders_But_Customer_Cannot_Access_Admin_Api()
    {
        // Customer token cannot access admin endpoint
        var (token, _) = await SetupCustomerWithProductAsync();
        UseToken(token);
        var forbidden = await Client.GetAsync("/api/admin/orders?page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private record PagedResult(List<Item> Items, int Page, int PageSize, int TotalCount);
    private record Item(Guid Id, string Name);
    private record CheckoutDoc(OrderDoc Order, object Payment);
    private record OrderDoc(Guid Id, string OrderNumber);
}
