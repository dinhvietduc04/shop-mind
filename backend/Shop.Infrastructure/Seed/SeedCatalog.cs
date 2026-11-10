using System.Text.Json;
using Shop.Application.Specs;
using Shop.Domain.Entities;
using Shop.Domain.Enums;

namespace Shop.Infrastructure.Seed;

/// <summary>
/// A3: the seed catalog in one place, so CI can validate every Specs value
/// against <see cref="SpecRegistry"/> without a database.
/// </summary>
public static class SeedCatalog
{
    public static IReadOnlyList<Category> Categories() =>
    [
        new() { Name = "Laptops", Slug = "laptops", Description = "Laptops and notebooks", IsActive = true },
        new() { Name = "Smartphones", Slug = "smartphones", Description = "Smartphones and mobiles", IsActive = true },
        new() { Name = "Monitors", Slug = "monitors", Description = "Monitors and displays", IsActive = true },
        new() { Name = "Keyboards", Slug = "keyboards", Description = "Keyboards", IsActive = true },
        new() { Name = "Mice", Slug = "mice", Description = "Mice and pointing devices", IsActive = true },
        new() { Name = "Headphones", Slug = "headphones", Description = "Headphones and audio", IsActive = true },
        new() { Name = "Components", Slug = "components", Description = "PC components", IsActive = true },
        new() { Name = "Accessories", Slug = "accessories", Description = "Accessories", IsActive = true },
    ];

    public static IReadOnlyList<SeedProduct> Products(IReadOnlyDictionary<string, Category> bySlug) =>
    [
        Product(bySlug["laptops"], "ThinkPad X1 Carbon Gen 11", "thinkpad-x1-carbon-gen-11", "LEN-X1C-G11", "Lenovo",
            "14\" business ultrabook, Intel i7, 16GB RAM, 512GB SSD.", 1499.00m, 25,
            Specs(("ram_gb", 16), ("storage_gb", 512), ("screen_in", 14), ("cpu", "Intel Core i7"),
                ("weight_kg", 1.12), ("thunderbolt", true))),
        Product(bySlug["laptops"], "MacBook Air M3", "macbook-air-m3", "APL-MBA-M3", "Apple",
            "13\" MacBook Air with M3 chip, 8GB RAM, 256GB SSD.", 1099.00m, 20,
            Specs(("ram_gb", 8), ("storage_gb", 256), ("screen_in", 13.6), ("cpu", "Apple M3"),
                ("weight_kg", 1.24))),
        Product(bySlug["smartphones"], "Galaxy S24 Ultra", "galaxy-s24-ultra", "SAM-S24U", "Samsung",
            "Flagship smartphone with 200MP camera.", 1199.99m, 40,
            Specs(("ram_gb", 12), ("storage_gb", 256), ("screen_in", 6.8), ("camera_mp", 200), ("s_pen", true))),
        Product(bySlug["smartphones"], "iPhone 15 Pro", "iphone-15-pro", "APL-IP15P", "Apple",
            "6.1\" iPhone 15 Pro, A17 Pro.", 999.00m, 35,
            Specs(("ram_gb", 8), ("storage_gb", 128), ("screen_in", 6.1), ("cpu", "A17 Pro"))),
        Product(bySlug["monitors"], "UltraSharp 27 4K Monitor", "ultrasharp-27-4k", "DEL-U2723QE", "Dell",
            "27\" 4K USB-C monitor.", 549.99m, 15,
            Specs(("screen_in", 27), ("resolution", "3840x2160"), ("usb_c", true), ("panel", "IPS"))),
        Product(bySlug["keyboards"], "MX Keys S Keyboard", "mx-keys-s", "LOG-MXKEYS-S", "Logitech",
            "Wireless illuminated keyboard.", 109.99m, 60,
            Specs(("wireless", true), ("backlit", true), ("layout", "full"))),
        Product(bySlug["mice"], "MX Master 3S Mouse", "mx-master-3s", "LOG-MXM3S", "Logitech",
            "Wireless performance mouse.", 99.99m, 80,
            Specs(("wireless", true), ("dpi_max", 8000), ("buttons", 7))),
        Product(bySlug["headphones"], "WH-1000XM5 Headphones", "wh-1000xm5", "SNY-WH1000XM5", "Sony",
            "Noise cancelling headphones.", 349.99m, 30,
            Specs(("anc", true), ("battery_h", 30), ("wireless", true))),
        Product(bySlug["components"], "RTX 4070 GPU", "rtx-4070", "NV-RTX4070", "NVIDIA",
            "12GB GDDR6X graphics card.", 599.99m, 12,
            Specs(("vram_gb", 12), ("interface", "PCIe 4.0"), ("tdp_w", 200))),
        Product(bySlug["accessories"], "USB-C Docking Station", "usb-c-dock", "ACC-USBCDOCK", "Anker",
            "7-in-1 USB-C hub.", 49.99m, 100,
            Specs(("ports", 7), ("4k_hdmi", true), ("pd_w", 100))),
    ];

    /// <summary>CI gate / seed-time guard: every populated key must exist for the category and match its type.</summary>
    public static void Validate()
    {
        var bySlug = Categories().ToDictionary(c => c.Slug, StringComparer.OrdinalIgnoreCase);
        foreach (var seed in Products(bySlug))
        {
            foreach (var spec in seed.Product.Specs.EnumerateObject())
            {
                var def = SpecRegistry.Find(spec.Name)
                    ?? throw new InvalidOperationException(
                        $"Seed product '{seed.Product.SKU}' uses unregistered spec '{spec.Name}'.");
                if (!def.Categories.Contains(seed.CategorySlug, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Seed product '{seed.Product.SKU}' uses spec '{spec.Name}' outside category '{seed.CategorySlug}'.");
                if (!SpecRegistry.IsValidValue(def, spec.Value))
                    throw new InvalidOperationException(
                        $"Seed product '{seed.Product.SKU}' has an invalid {def.Type} value for spec '{spec.Name}'.");
            }
        }
    }

    private static SeedProduct Product(Category category, string name, string slug, string sku, string brand,
        string description, decimal price, int stock, JsonElement specs) =>
        new(category.Slug, new Product
        {
            Name = name,
            Slug = slug,
            SKU = sku,
            CategoryId = category.Id,
            Brand = brand,
            Description = description,
            Price = price,
            Status = ProductStatus.Active,
            Specs = specs,
        }, stock);

    private static JsonElement Specs(params (string Key, object Value)[] values) =>
        JsonSerializer.SerializeToElement(values.ToDictionary(v => v.Key, v => v.Value));
}

public sealed record SeedProduct(string CategorySlug, Product Product, int Stock);
