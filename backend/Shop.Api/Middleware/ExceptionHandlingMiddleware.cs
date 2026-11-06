using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Shop.Domain.Exceptions;

namespace Shop.Api.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex, logger);
        }
    }

    private static Task HandleAsync(HttpContext context, Exception ex, ILogger logger)
    {
        // Unwrap unique-constraint violations (e.g. concurrent cart inserts) -> 409
        if (ex is DbUpdateException dbEx && IsUniqueViolation(dbEx))
        {
            return WriteAsync(context, 409, "Conflict", "Resource already exists (concurrent request). Please retry.");
        }

        // FK violation: most commonly a stale JWT (user row gone after DB reseed)
        // or a client-supplied product/category id that no longer exists.
        if (ex is DbUpdateException fkEx && IsFkViolation(fkEx, out var fkName))
        {
            if (fkName.Contains("Carts", StringComparison.OrdinalIgnoreCase))
                return WriteAsync(context, 401, "Session expired (user no longer exists). Please login again.", null, "Unauthorized");
            return WriteAsync(context, 400, "Referenced resource does not exist.", null, "ForeignKeyViolation");
        }

        var (status, code, message, errors) = ex switch
        {
            ValidationException vex => (400, "ValidationFailed", "Validation failed",
                (object?)vex.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => ToCamel(g.Key), g => g.Select(e => e.ErrorMessage).ToArray())),
            DomainValidationException dex => (400, dex.Code, dex.Message, null),
            EmailAlreadyExistsException eex => (409, eex.Code, eex.Message, null),
            InvalidCredentialsException icex => (401, icex.Code, icex.Message, null),
            UnauthorizedOrderAccessException uex => (403, uex.Code, uex.Message, null),
            UnauthorizedAccessException => (401, "Unauthorized", ex.Message, null),
            InsufficientStockException isex => (409, isex.Code, isex.Message, null),
            InvalidOrderStateException ioex => (422, ioex.Code, ioex.Message, null),
            PaymentFailedException pfex => (402, pfex.Code, pfex.Message, null),
            EmptyCartException ecex => (400, ecex.Code, ecex.Message, null),
            InactiveProductException ipex => (422, ipex.Code, ipex.Message, null),
            DbUpdateConcurrencyException => (409, "ConcurrencyConflict",
                "Resource was modified concurrently. Please retry.", null),
            ProductNotFoundException pn => (404, pn.Code, pn.Message, null),
            CategoryNotFoundException cn => (404, cn.Code, cn.Message, null),
            CartItemNotFoundException ci => (404, ci.Code, ci.Message, null),
            OrderNotFoundException on => (404, on.Code, on.Message, null),
            NotFoundException nf => (404, nf.Code, nf.Message, null),
            ShopDomainException dex => (400, dex.Code, dex.Message, null),
            _ => (500, "InternalError", "An unexpected error occurred.", null)
        };

        if (status == 500)
            logger.LogError(ex, "Unhandled exception");

        return WriteAsync(context, status, message, errors, code);
    }

    private static Task WriteAsync(HttpContext context, int status, string message, object? errors, string code = "")
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = status;

        object payload = errors is null
            ? new { title = message, status, code }
            : new { title = message, status, errors };

        // Spec example for validation errors: { title, status, errors: { quantity: [...] } }
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        while (inner is not null)
        {
            // Npgsql PostgresException SqlState 23505 without hard dependency
            if (inner.GetType().Name == "PostgresException")
            {
                var prop = inner.GetType().GetProperty("SqlState");
                if (prop?.GetValue(inner) as string == "23505") return true;
            }
            if (inner.Message.Contains("23505") || inner.Message.Contains("duplicate key"))
                return true;
            inner = inner.InnerException;
        }
        return ex.Message.Contains("duplicate key");
    }

    private static bool IsFkViolation(DbUpdateException ex, out string constraint)
    {
        constraint = "";
        var inner = ex.InnerException;
        while (inner is not null)
        {
            if (inner.GetType().Name == "PostgresException")
            {
                var state = inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string;
                if (state == "23503")
                {
                    constraint = inner.GetType().GetProperty("ConstraintName")?.GetValue(inner) as string ?? "";
                    return true;
                }
            }
            if (inner.Message.Contains("23503") || inner.Message.Contains("violates foreign key"))
            {
                constraint = inner.Message;
                return true;
            }
            inner = inner.InnerException;
        }
        return false;
    }

    private static string ToCamel(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var parts = s.Split('.');
        var last = parts[^1];
        return char.ToLowerInvariant(last[0]) + last[1..];
    }
}
