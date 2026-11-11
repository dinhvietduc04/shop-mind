using System.Security.Claims;
using System.Text.RegularExpressions;

namespace Shop.Application.Mcp;

/// <summary>
/// A1: builds the MCP identity context from a delegated JWT (HTTP) or explicit
/// env values (STDIO dev). Correlation ids are bounded/printable so they are
/// safe in logs and structured error envelopes.
/// </summary>
public static class McpToolContextFactory
{
    public const string DevUserIdEnv = "MCP_DEV_USER_ID";
    public const string DevRoleEnv = "MCP_DEV_ROLE";
    public const string DevScopeEnv = "MCP_DEV_SCOPE";
    public const string DevCorrelationIdEnv = "MCP_DEV_CORRELATION_ID";

    private static readonly Regex AcceptableCorrelation =
        new(@"^[A-Za-z0-9\-_]{1,64}$", RegexOptions.Compiled);

    public static McpToolContext FromPrincipal(
        ClaimsPrincipal? principal,
        string? correlationId,
        string? idempotencyKey,
        string? clientAddress)
    {
        var authenticated = principal?.Identity?.IsAuthenticated == true;
        var userId = authenticated
            ? ReadGuid(principal!, ClaimTypes.NameIdentifier) ?? ReadGuid(principal!, "sub")
            : null;
        var role = authenticated
            ? NormalizeOptional(principal!.FindFirst(ClaimTypes.Role)?.Value ?? principal.FindFirst("role")?.Value)
            : null;
        var scope = authenticated ? NormalizeOptional(principal!.FindFirst("scope")?.Value) : null;

        return Build(userId, role, scope, correlationId, idempotencyKey, clientAddress);
    }

    public static McpToolContext FromStdio(
        Func<string, string?> getEnvironmentVariable,
        string? correlationId,
        string? idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        var userId = TryParseGuid(getEnvironmentVariable(DevUserIdEnv));
        var role = NormalizeOptional(getEnvironmentVariable(DevRoleEnv));
        var scope = NormalizeOptional(getEnvironmentVariable(DevScopeEnv))
                    ?? (userId.HasValue ? "agent" : null);
        var correlation = correlationId ?? getEnvironmentVariable(DevCorrelationIdEnv);

        return Build(userId, role, scope, correlation, idempotencyKey, "stdio");
    }

    private static McpToolContext Build(
        Guid? userId,
        string? role,
        string? scope,
        string? correlationId,
        string? idempotencyKey,
        string? clientAddress)
    {
        var partition = userId is { } id
            ? $"user:{id:D}"
            : $"anon:{NormalizeOptional(clientAddress) ?? "unknown"}";

        return new McpToolContext(
            userId,
            role,
            scope,
            NormalizeCorrelation(correlationId),
            NormalizeIdempotencyKey(idempotencyKey),
            partition);
    }

    private static Guid? ReadGuid(ClaimsPrincipal principal, string claimType) =>
        TryParseGuid(principal.FindFirst(claimType)?.Value);

    private static Guid? TryParseGuid(string? value) =>
        Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;

    private static string NormalizeCorrelation(string? value) =>
        !string.IsNullOrWhiteSpace(value) && AcceptableCorrelation.IsMatch(value)
            ? value
            : Guid.NewGuid().ToString("N");

    private static string? NormalizeIdempotencyKey(string? value)
    {
        var trimmed = NormalizeOptional(value);
        return trimmed is { Length: <= 128 } ? trimmed : null;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
