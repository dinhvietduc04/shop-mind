namespace Shop.Application.Dtos;

public record CreateProductImageRequest(string Url, string? AltText, int DisplayOrder);
