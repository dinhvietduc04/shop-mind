using FluentValidation;
using Shop.Application.Dtos;

namespace Shop.Application.Validation;

public class AddCartItemValidator : AbstractValidator<AddCartItemRequest>
{
    public AddCartItemValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
    }
}
