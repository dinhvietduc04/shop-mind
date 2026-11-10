namespace Shop.Application.Dtos;

public record CategoryDto(Guid Id, string Name, string Slug, string? Description, bool IsActive);
