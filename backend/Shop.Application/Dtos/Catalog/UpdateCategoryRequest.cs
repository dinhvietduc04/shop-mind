namespace Shop.Application.Dtos;

public record UpdateCategoryRequest(string Name, string? Slug, string? Description, bool IsActive);
