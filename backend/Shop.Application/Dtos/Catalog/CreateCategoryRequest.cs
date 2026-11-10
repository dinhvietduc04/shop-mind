namespace Shop.Application.Dtos;

public record CreateCategoryRequest(string Name, string? Slug, string? Description, bool IsActive = true);
