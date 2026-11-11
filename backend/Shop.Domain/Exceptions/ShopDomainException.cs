namespace Shop.Domain.Exceptions;

public class ShopDomainException : Exception
{
    public string Code { get; }
    protected ShopDomainException(string code, string message, Exception? inner = null)
        : base(message, inner) => Code = code;
}
