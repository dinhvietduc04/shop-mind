namespace Shop.Domain.Enums;

public enum UserRole
{
    Customer = 0,
    Admin = 1
}

public enum ProductStatus
{
    Draft = 0,
    Active = 1,
    Inactive = 2
}

public enum OrderStatus
{
    Pending = 0,
    Paid = 1,
    Processing = 2,
    Shipped = 3,
    Delivered = 4,
    Cancelled = 5,
    PaymentFailed = 6
}

public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2
}
