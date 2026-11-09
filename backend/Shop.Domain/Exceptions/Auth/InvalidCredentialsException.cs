namespace Shop.Domain.Exceptions;

public class InvalidCredentialsException : ShopDomainException
{
    public InvalidCredentialsException()
        : base("InvalidCredentials", "Invalid email or password.") { }
}
