using Shop.Domain.Enums;

namespace Shop.Application.Dtos;

public record UserDto(Guid Id, string Email, string FirstName, string LastName, string? PhoneNumber, UserRole Role);
public record RegisterRequest(string Email, string Password, string FirstName, string LastName, string? PhoneNumber);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, UserDto User);
public record UpdateProfileRequest(string FirstName, string LastName, string? PhoneNumber);

public record CategoryDto(Guid Id, string Name, string Slug, string? Description, bool IsActive);
public record CreateCategoryRequest(string Name, string? Slug, string? Description, bool IsActive = true);
public record UpdateCategoryRequest(string Name, string? Slug, string? Description, bool IsActive);

public record ProductImageDto(Guid Id, string Url, string? AltText, int DisplayOrder);
public record ProductDto(
    Guid Id,
    string Name,
    string Slug,
    string SKU,
    string? Brand,
    decimal Price,
    ProductStatus Status,
    Guid CategoryId,
    string? CategoryName,
    int AvailableStock,
    IReadOnlyList<ProductImageDto> Images);
public record ProductDetailDto(
    Guid Id,
    string Name,
    string Slug,
    string SKU,
    string? Description,
    string? Brand,
    decimal Price,
    ProductStatus Status,
    Guid CategoryId,
    string? CategoryName,
    int AvailableStock,
    IReadOnlyList<ProductImageDto> Images,
    DateTime CreatedAt);

public record ProductQueryParams(
    string? Search,
    string? Category, // slug or id
    string? Brand,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool? InStockOnly,
    string? Sort, // price_asc, price_desc, newest, name_asc, name_desc
    int Page = 1,
    int PageSize = 20,
    bool IncludeInactive = false);

public record CreateProductImageRequest(string Url, string? AltText, int DisplayOrder);
public record CreateProductRequest(
    string Name,
    string? Slug,
    string SKU,
    Guid CategoryId,
    string? Description,
    string? Brand,
    decimal Price,
    ProductStatus Status,
    List<CreateProductImageRequest>? Images,
    int InitialStock);
public record UpdateProductRequest(
    string Name,
    string? Slug,
    string SKU,
    Guid CategoryId,
    string? Description,
    string? Brand,
    decimal Price,
    ProductStatus Status,
    List<CreateProductImageRequest>? Images);

public record CartItemDto(Guid Id, Guid ProductId, string ProductName, string SKU, int Quantity, decimal UnitPrice, decimal Subtotal, string? ImageUrl);
public record CartDto(Guid Id, IReadOnlyList<CartItemDto> Items, decimal Subtotal, int TotalQuantity);
public record AddCartItemRequest(Guid ProductId, int Quantity);
public record UpdateCartItemRequest(int Quantity);

public record ShippingAddressDto(string FullName, string Phone, string Address, string City);
public record CheckoutRequest(ShippingAddressDto ShippingAddress, string PaymentMethod = "fake", bool SimulateFailure = false, bool SimulateTimeout = false);
public record PaymentDto(Guid Id, decimal Amount, string Status, string? TransactionId);
public record OrderItemDto(Guid ProductId, string ProductName, string SKU, decimal UnitPrice, int Quantity, decimal Subtotal);
public record OrderDto(
    Guid Id,
    string OrderNumber,
    string Status,
    decimal Subtotal,
    decimal ShippingFee,
    decimal TotalAmount,
    ShippingAddressDto ShippingAddress,
    DateTime CreatedAt,
    IReadOnlyList<OrderItemDto> Items,
    PaymentDto? Payment);
public record CheckoutResponse(OrderDto Order, PaymentDto Payment);

public record InventoryDto(Guid ProductId, string ProductName, string SKU, int Quantity, int ReservedQuantity, int AvailableQuantity);
public record UpdateInventoryRequest(int Quantity);

public record UpdateOrderStatusRequest(string Status);
