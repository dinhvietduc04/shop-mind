using Microsoft.EntityFrameworkCore;
using Shop.Application.Dtos;
using Shop.Application.Interfaces;
using Shop.Application.Persistence;
using Shop.Application.Validation;
using Shop.Domain.Entities;
using Shop.Domain.Exceptions;

namespace Shop.Application.Services;

public class CategoryService(IAppDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> ListPublicAsync(CancellationToken ct = default)
        => await db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.Description, c.IsActive))
            .ToListAsync(ct);

    public async Task<CategoryDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CategoryNotFoundException(id);
        return c.ToDto();
    }

    public async Task<IReadOnlyList<CategoryDto>> ListAdminAsync(CancellationToken ct = default)
        => await db.Categories.OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.Description, c.IsActive))
            .ToListAsync(ct);

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct = default)
    {
        var slug = string.IsNullOrWhiteSpace(request.Slug) ? SlugHelper.Slugify(request.Name) : SlugHelper.Slugify(request.Slug);
        if (await db.Categories.AnyAsync(c => c.Name == request.Name.Trim() || c.Slug == slug, ct))
            throw new DomainValidationException("Category name or slug already exists.");
        var c = new Category
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description?.Trim(),
            IsActive = request.IsActive
        };
        db.Categories.Add(c);
        await db.SaveChangesAsync(ct);
        return c.ToDto();
    }

    public async Task<CategoryDto> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct = default)
    {
        var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CategoryNotFoundException(id);
        var slug = string.IsNullOrWhiteSpace(request.Slug) ? SlugHelper.Slugify(request.Name) : SlugHelper.Slugify(request.Slug);
        if (await db.Categories.AnyAsync(x => x.Id != id && (x.Name == request.Name.Trim() || x.Slug == slug), ct))
            throw new DomainValidationException("Category name or slug already exists.");
        c.Name = request.Name.Trim();
        c.Slug = slug;
        c.Description = request.Description?.Trim();
        c.IsActive = request.IsActive;
        c.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return c.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.Categories.Include(x => x.Products).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new CategoryNotFoundException(id);
        if (c.Products.Any())
            throw new DomainValidationException("Cannot delete category that contains products. Deactivate it instead.");
        db.Categories.Remove(c);
        await db.SaveChangesAsync(ct);
    }
}
