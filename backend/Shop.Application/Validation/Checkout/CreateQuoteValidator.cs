using FluentValidation;
using Shop.Application.Dtos;

namespace Shop.Application.Validation;

/// <summary>B5 (ADR-0003): the quote POST's address validation — the rules CheckoutValidator enforced for the deleted legacy endpoint, unchanged.</summary>
public class CreateQuoteValidator : AbstractValidator<CreateQuoteRequest>
{
    public CreateQuoteValidator()
    {
        RuleFor(x => x.ShippingAddress).NotNull();
        RuleFor(x => x.ShippingAddress.FullName).NotEmpty().MaximumLength(200).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress.Phone).NotEmpty().MaximumLength(32).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress.Address).NotEmpty().MaximumLength(500).When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress.City).NotEmpty().MaximumLength(200).When(x => x.ShippingAddress is not null);
    }
}
