using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Infrastructure.Persistence;

namespace Shop.IntegrationTests;

/// <summary>
/// A3: facets are derived from registry ∩ populated Active products, scoped to
/// one category, and never expose booleans or unregistered jsonb keys.
/// </summary>
public class SpecFacetsTests : IntegrationTestBase<ShopWebFactory>
{
    public SpecFacetsTests(ShopWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Facets_include_only_registered_specs_populated_by_active_products()
    {
        await AddProductAsync("laptops", ProductStatus.Draft, new { camera_mp = 48, bogus_spec = "x" });
        await AddProductAsync("laptops", ProductStatus.Active, new { ram_gb = 32, bogus_spec = "x" });

        var facets = await FacetsAsync("laptops");
        var keys = facets.Select(f => f.GetProperty("key").GetString()).ToHashSet();

        Assert.Equal(["cpu", "ram_gb", "screen_in", "storage_gb", "weight_kg"],
            keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => k!).ToArray());
        Assert.DoesNotContain("thunderbolt", keys); // bool facets are not offered
        Assert.DoesNotContain("bogus_spec", keys); // unregistered keys do not exist
        Assert.DoesNotContain("camera_mp", keys); // populated by a draft only
    }

    [Fact]
    public async Task Facets_are_category_specific()
    {
        var headphoneKeys = (await FacetsAsync("headphones"))
            .Select(f => f.GetProperty("key").GetString())
            .ToHashSet();

        Assert.Contains("battery_h", headphoneKeys);
        Assert.Single(headphoneKeys); // anc + wireless are bools
    }

    [Fact]
    public async Task Range_and_enum_facets_carry_the_contract_shape()
    {
        var battery = (await FacetsAsync("headphones"))
            .Single(f => f.GetProperty("key").GetString() == "battery_h");
        Assert.Equal("range", battery.GetProperty("type").GetString());
        Assert.Equal("h", battery.GetProperty("unit").GetString());
        Assert.Equal(30, battery.GetProperty("min").GetDouble());
        Assert.Equal(30, battery.GetProperty("max").GetDouble());
        Assert.Equal(1, battery.GetProperty("count").GetInt32());
        Assert.Null(Optional(battery, "values"));

        var panel = (await FacetsAsync("monitors"))
            .Single(f => f.GetProperty("key").GetString() == "panel");
        Assert.Equal("enum", panel.GetProperty("type").GetString());
        Assert.Equal(["IPS"], panel.GetProperty("values").EnumerateArray().Select(v => v.GetString()!).ToArray());
        Assert.Equal(1, panel.GetProperty("count").GetInt32());
        Assert.Null(Optional(panel, "min"));
        Assert.Null(Optional(panel, "max"));
    }

    [Fact]
    public async Task Facets_without_a_category_scan_all_active_products()
    {
        var keys = (await FacetsAsync(null))
            .Select(f => f.GetProperty("key").GetString())
            .ToHashSet();

        Assert.Contains("ram_gb", keys);
        Assert.Contains("battery_h", keys);
        Assert.Contains("vram_gb", keys);
        Assert.Contains("panel", keys);
        Assert.DoesNotContain("thunderbolt", keys);
        Assert.DoesNotContain("anc", keys);
    }

    private static JsonElement? Optional(JsonElement facet, string name) =>
        facet.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    private async Task AddProductAsync(string categorySlug, ProductStatus status, object specs)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var categoryId = await db.Categories.Where(c => c.Slug == categorySlug).Select(c => c.Id).SingleAsync();
        var sku = $"FACET-{Guid.NewGuid():N}";
        db.Products.Add(new Product
        {
            Name = "Facet probe",
            Slug = sku.ToLowerInvariant(),
            SKU = sku,
            CategoryId = categoryId,
            Price = 10m,
            Status = status,
            Specs = JsonSerializer.SerializeToElement(specs),
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<JsonElement>> FacetsAsync(string? category)
    {
        var url = category is null ? "/api/products/facets" : $"/api/products/facets?category={category}";
        var res = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }
}
