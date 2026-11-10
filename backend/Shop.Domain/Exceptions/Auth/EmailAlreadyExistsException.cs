namespace Shop.Domain.Exceptions;

public class EmailAlreadyExistsException : ShopDomainException
{
    public EmailAlreadyExistsException(string email)
        : base("EmailAlreadyExists", $"Email '{email}' is already registered.") { }
}
