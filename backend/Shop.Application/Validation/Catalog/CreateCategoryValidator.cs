using FluentValidation;
using Shop.Application.Dtos;

namespace Shop.Application.Validation;

public class CreateCategoryValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).MaximumLength(200).When(x => x.Slug is not null);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
    }
}
