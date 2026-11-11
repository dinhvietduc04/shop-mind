using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Shop.Domain.Entities;

namespace Shop.Application.Specs;

/// <summary>
/// A3: translates validated spec clauses into PostgreSQL <c>jsonb</c>
/// predicates. Values are extracted with the <c>-&gt;&gt;</c> DOM traversal so
/// filtering, paging and counts all happen in SQL.
/// </summary>
public static class SpecFilter
{
    private static readonly MethodInfo GetProperty =
        typeof(JsonElement).GetMethod(nameof(JsonElement.GetProperty), [typeof(string)])!;
    private static readonly MethodInfo GetDouble =
        typeof(JsonElement).GetMethod(nameof(JsonElement.GetDouble))!;
    private static readonly MethodInfo GetString =
        typeof(JsonElement).GetMethod(nameof(JsonElement.GetString))!;
    private static readonly MethodInfo GetBoolean =
        typeof(JsonElement).GetMethod(nameof(JsonElement.GetBoolean))!;
    private static readonly MethodInfo ToLower =
        typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    public static IQueryable<Product> Apply(IQueryable<Product> query, IReadOnlyList<SpecClause> clauses)
    {
        foreach (var clause in clauses)
            query = query.Where(Build(clause));
        return query;
    }

    private static Expression<Func<Product, bool>> Build(SpecClause clause)
    {
        var product = Expression.Parameter(typeof(Product), "p");
        var element = Expression.Call(
            Expression.Property(product, nameof(Product.Specs)),
            GetProperty,
            Expression.Constant(clause.Key));

        Expression body = clause switch
        {
            { Op: SpecOp.Gte, Value: double n } => Expression.GreaterThanOrEqual(
                Expression.Call(element, GetDouble), Expression.Constant(n)),
            { Op: SpecOp.Lte, Value: double n } => Expression.LessThanOrEqual(
                Expression.Call(element, GetDouble), Expression.Constant(n)),
            { Op: SpecOp.Eq, Value: double n } => Expression.Equal(
                Expression.Call(element, GetDouble), Expression.Constant(n)),
            { Op: SpecOp.Eq, Value: bool b } => Expression.Equal(
                Expression.Call(element, GetBoolean), Expression.Constant(b)),
            { Op: SpecOp.Eq, Value: string s } => TextEquals(element, s),
            { Op: SpecOp.In, Value: IReadOnlyList<string> values } => TextIn(element, values),
            _ => throw new NotSupportedException($"Spec clause {clause.Key}:{clause.Op} is not supported."),
        };

        return Expression.Lambda<Func<Product, bool>>(body, product);
    }

    /// <summary>Case-insensitive enum equality (mirrors the client codec).</summary>
    private static Expression TextEquals(Expression element, string value)
    {
        var text = Expression.Call(element, GetString);
        return Expression.Equal(
            Expression.Call(text, ToLower),
            Expression.Constant(value.ToLowerInvariant()));
    }

    private static Expression TextIn(Expression element, IReadOnlyList<string> values)
    {
        Expression? body = null;
        foreach (var value in values)
        {
            var match = TextEquals(element, value);
            body = body is null ? match : Expression.OrElse(body, match);
        }
        return body ?? Expression.Constant(false);
    }
}
