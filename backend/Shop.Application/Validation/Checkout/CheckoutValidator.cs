using FluentValidation;
using Shop.Application.Dtos;

namespace Shop.Application.Validation;

public class CheckoutValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutValidator()
    {
        RuleFor(x => x.ShippingAddress).NotNull();
        RuleFor(x => x.ShippingAddress.FullName).NotEmpty().MaximumLength(200).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress.Phone).NotEmpty().MaximumLength(32).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress.Address).NotEmpty().MaximumLength(500).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress.City).NotEmpty().MaximumLength(200).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.PaymentMethod).NotEmpty().Must(m => m == "fake").WithMessage("Only 'fake' payment is supported in V1.");
    }
}
