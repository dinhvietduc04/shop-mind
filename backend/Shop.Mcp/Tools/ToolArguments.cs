using FluentValidation;
using FluentValidation.Results;
using Shop.Application.Mcp;

namespace Shop.Mcp.Tools;

/// <summary>
/// A2: the shared boundary checks every new tool needs. The services trust their
/// arguments, so ids, ownership, and paging are validated once here and the tool
/// method stays a single Application call.
/// </summary>
internal static class ToolArguments
{
    public static Guid RequireUserId(McpToolContext context) =>
        context.UserId ?? throw new UnauthorizedAccessException(
            "A valid user token is required for this tool.");

    public static Guid RequireId(Guid id, string property) =>
        id == Guid.Empty ? throw Invalid(property, $"{property} is required.") : id;

    public static void ValidatePage(int page, int pageSize, int maxPageSize)
    {
        var failures = new List<ValidationFailure>();
        if (page < 1)
            failures.Add(new ValidationFailure("page", "page must be at least 1."));
        if (pageSize < 1 || pageSize > maxPageSize)
            failures.Add(new ValidationFailure("pageSize", $"pageSize must be between 1 and {maxPageSize}."));
        if (failures.Count > 0) throw new ValidationException(failures);
    }

    public static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
