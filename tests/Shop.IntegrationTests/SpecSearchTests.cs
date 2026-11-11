using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Infrastructure.Persistence;

namespace Shop.IntegrationTests;

/// <summary>
/// A3: spec filters must run against the actual PostgreSQL <c>jsonb</c> column
/// (not an in-memory post-filter), through the public catalog API.
/// </summary>
public class SpecSearchTests : IntegrationTestBase<ShopWebFactory>
{
    public SpecSearchTests(ShopWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Numeric_gte_filter_selects_only_matching_active_products()
    {
        var res = await Client.GetAsync("/api/products?category=laptops&specs=ram_gb:gte:16&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("ThinkPad X1 Carbon Gen 11", items[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Enum_eq_filter_is_case_insensitive()
    {
        var items = await SearchAsync("category=monitors&specs=panel:eq:ips");

        Assert.Single(items);
        Assert.Equal("UltraSharp 27 4K Monitor", items[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Enum_in_filter_matches_any_listed_value()
    {
        var items = await SearchAsync("category=laptops&specs=cpu:in:Apple M3|Intel Core i7");

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task Bool_eq_filter_matches_only_true_values()
    {
        var items = await SearchAsync("category=headphones&specs=anc:eq:true");

        Assert.Single(items);
        Assert.Equal("WH-1000XM5 Headphones", items[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Lte_filter_matches_lower_values()
    {
        var items = await SearchAsync("category=laptops&specs=ram_gb:lte:8");

        Assert.Single(items);
        Assert.Equal("MacBook Air M3", items[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Multiple_clauses_are_anded()
    {
        var items = await SearchAsync("category=laptops&specs=ram_gb:gte:8;storage_gb:gte:512");

        Assert.Single(items);
        Assert.Equal("ThinkPad X1 Carbon Gen 11", items[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Product_response_carries_the_specs_map()
    {
        var items = await SearchAsync("category=laptops&specs=ram_gb:gte:16");

        var specs = Assert.Single(items).GetProperty("specs");
        Assert.Equal(16, specs.GetProperty("ram_gb").GetInt32());
        Assert.Equal("Intel Core i7", specs.GetProperty("cpu").GetString());
    }

    [Theory]
    [InlineData("bogus:gte:1")]
    [InlineData("ram_gb:gte:lots")]
    [InlineData("ram_gb:in:8|16")]
    [InlineData("panel:gte:3")]
    public async Task Invalid_spec_filters_are_rejected_with_400(string specs)
    {
        var res = await Client.GetAsync($"/api/products?specs={Uri.EscapeDataString(specs)}");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("InvalidSpecFilter", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Spec_from_another_category_is_rejected_when_category_is_explicit()
    {
        var res = await Client.GetAsync("/api/products?category=laptops&specs=anc:eq:true");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("InvalidSpecFilter", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Inactive_products_never_match_spec_filters()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var categoryId = await db.Categories
                .Where(c => c.Slug == "laptops")
                .Select(c => c.Id)
                .SingleAsync();
            var sku = $"DRAFT-{Guid.NewGuid():N}";
            db.Products.Add(new Product
            {
                Name = "Hidden 64GB Laptop",
                Slug = sku.ToLowerInvariant(),
                SKU = sku,
                CategoryId = categoryId,
                Price = 1m,
                Status = ProductStatus.Draft,
                Specs = JsonSerializer.SerializeToElement(new { ram_gb = 64 }),
            });
            await db.SaveChangesAsync();
        }

        var items = await SearchAsync("category=laptops&specs=ram_gb:gte:64");

        Assert.Empty(items);
    }

    private async Task<List<JsonElement>> SearchAsync(string query)
    {
        var res = await Client.GetAsync($"/api/products?pageSize=100&{query}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
    }
}
