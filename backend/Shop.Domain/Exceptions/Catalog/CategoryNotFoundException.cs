namespace Shop.Domain.Exceptions;

public class CategoryNotFoundException : NotFoundException
{
    public CategoryNotFoundException(Guid? id = null)
        : base("CategoryNotFound", id is null ? "Category not found." : $"Category '{id}' not found.") { }
}
