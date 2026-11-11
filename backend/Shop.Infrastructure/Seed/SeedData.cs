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
            SeedCatalog.Validate();

            var categories = SeedCatalog.Categories().ToList();
            db.Categories.AddRange(categories);
            await db.SaveChangesAsync(ct);

            var bySlug = categories.ToDictionary(c => c.Slug);
            foreach (var seed in SeedCatalog.Products(bySlug))
            {
                var p = seed.Product;
                p.Images.Add(new ProductImage { ProductId = p.Id, Url = $"https://picsum.photos/seed/{p.Slug}/600/400", AltText = p.Name, DisplayOrder = 0 });
                p.Inventory = new Inventory { ProductId = p.Id, Quantity = seed.Stock, ReservedQuantity = 0 };
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
