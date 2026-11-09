using Shop.Domain.Enums;

namespace Shop.Application.Dtos;

public record UserDto(Guid Id, string Email, string FirstName, string LastName, string? PhoneNumber, UserRole Role);
