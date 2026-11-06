using Microsoft.EntityFrameworkCore;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure.Seed;

public static class SeedData
{
    public static async Task EnsureSeededAsync(AppDbContext db, IPasswordHasher hasher, CancellationToken ct = default)
    {
        if (!await db.Categories.AnyAsync(ct))
        {
            var categories = new List<Category>
            {
                new() { Name = "Laptops", Slug = "laptops", Description = "Laptops and notebooks", IsActive = true },
                new() { Name = "Smartphones", Slug = "smartphones", Description = "Smartphones and mobiles", IsActive = true },
                new() { Name = "Monitors", Slug = "monitors", Description = "Monitors and displays", IsActive = true },
                new() { Name = "Keyboards", Slug = "keyboards", Description = "Keyboards", IsActive = true },
                new() { Name = "Mice", Slug = "mice", Description = "Mice and pointing devices", IsActive = true },
                new() { Name = "Headphones", Slug = "headphones", Description = "Headphones and audio", IsActive = true },
                new() { Name = "Components", Slug = "components", Description = "PC components", IsActive = true },
                new() { Name = "Accessories", Slug = "accessories", Description = "Accessories", IsActive = true },
            };
            db.Categories.AddRange(categories);
            await db.SaveChangesAsync(ct);

            var bySlug = categories.ToDictionary(c => c.Slug);
            var products = new List<(Product p, int stock)>
            {
                (new Product { Name = "ThinkPad X1 Carbon Gen 11", Slug = "thinkpad-x1-carbon-gen-11", SKU = "LEN-X1C-G11", CategoryId = bySlug["laptops"].Id, Brand = "Lenovo", Description = "14\" business ultrabook, Intel i7, 16GB RAM, 512GB SSD.", Price = 1499.00m, Status = ProductStatus.Active }, 25),
                (new Product { Name = "MacBook Air M3", Slug = "macbook-air-m3", SKU = "APL-MBA-M3", CategoryId = bySlug["laptops"].Id, Brand = "Apple", Description = "13\" MacBook Air with M3 chip, 8GB RAM, 256GB SSD.", Price = 1099.00m, Status = ProductStatus.Active }, 20),
                (new Product { Name = "Galaxy S24 Ultra", Slug = "galaxy-s24-ultra", SKU = "SAM-S24U", CategoryId = bySlug["smartphones"].Id, Brand = "Samsung", Description = "Flagship smartphone with 200MP camera.", Price = 1199.99m, Status = ProductStatus.Active }, 40),
                (new Product { Name = "iPhone 15 Pro", Slug = "iphone-15-pro", SKU = "APL-IP15P", CategoryId = bySlug["smartphones"].Id, Brand = "Apple", Description = "6.1\" iPhone 15 Pro, A17 Pro.", Price = 999.00m, Status = ProductStatus.Active }, 35),
                (new Product { Name = "UltraSharp 27 4K Monitor", Slug = "ultrasharp-27-4k", SKU = "DEL-U2723QE", CategoryId = bySlug["monitors"].Id, Brand = "Dell", Description = "27\" 4K USB-C monitor.", Price = 549.99m, Status = ProductStatus.Active }, 15),
                (new Product { Name = "MX Keys S Keyboard", Slug = "mx-keys-s", SKU = "LOG-MXKEYS-S", CategoryId = bySlug["keyboards"].Id, Brand = "Logitech", Description = "Wireless illuminated keyboard.", Price = 109.99m, Status = ProductStatus.Active }, 60),
                (new Product { Name = "MX Master 3S Mouse", Slug = "mx-master-3s", SKU = "LOG-MXM3S", CategoryId = bySlug["mice"].Id, Brand = "Logitech", Description = "Wireless performance mouse.", Price = 99.99m, Status = ProductStatus.Active }, 80),
                (new Product { Name = "WH-1000XM5 Headphones", Slug = "wh-1000xm5", SKU = "SNY-WH1000XM5", CategoryId = bySlug["headphones"].Id, Brand = "Sony", Description = "Noise cancelling headphones.", Price = 349.99m, Status = ProductStatus.Active }, 30),
                (new Product { Name = "RTX 4070 GPU", Slug = "rtx-4070", SKU = "NV-RTX4070", CategoryId = bySlug["components"].Id, Brand = "NVIDIA", Description = "12GB GDDR6X graphics card.", Price = 599.99m, Status = ProductStatus.Active }, 12),
                (new Product { Name = "USB-C Docking Station", Slug = "usb-c-dock", SKU = "ACC-USBCDOCK", CategoryId = bySlug["accessories"].Id, Brand = "Anker", Description = "7-in-1 USB-C hub.", Price = 49.99m, Status = ProductStatus.Active }, 100),
            };

            foreach (var (p, stock) in products)
            {
                p.Images.Add(new ProductImage { ProductId = p.Id, Url = $"https://picsum.photos/seed/{p.Slug}/600/400", AltText = p.Name, DisplayOrder = 0 });
                p.Inventory = new Inventory { ProductId = p.Id, Quantity = stock, ReservedQuantity = 0 };
                db.Products.Add(p);
            }
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Users.AnyAsync(u => u.Email == "admin@shopmind.local", ct))
        {
            var admin = new User
            {
                Email = "admin@shopmind.local",
                PasswordHash = hasher.Hash("Admin123!"),
                FirstName = "Shop",
                LastName = "Admin",
                Role = UserRole.Admin,
            };
            db.Users.Add(admin);
            db.Carts.Add(new Cart { UserId = admin.Id });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Users.AnyAsync(u => u.Email == "customer@shopmind.local", ct))
        {
            var customer = new User
            {
                Email = "customer@shopmind.local",
                PasswordHash = hasher.Hash("Customer123!"),
                FirstName = "Demo",
                LastName = "Customer",
                Role = UserRole.Customer,
            };
            db.Users.Add(customer);
            db.Carts.Add(new Cart { UserId = customer.Id });
            await db.SaveChangesAsync(ct);
        }
    }
}
