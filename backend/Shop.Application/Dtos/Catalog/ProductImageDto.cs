namespace Shop.Application.Dtos;

public record ProductImageDto(Guid Id, string Url, string? AltText, int DisplayOrder);
