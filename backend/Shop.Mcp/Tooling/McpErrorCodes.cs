namespace Shop.Mcp.Tooling;

/// <summary>A1: the structured error codes returned in the MCP error envelope.</summary>
public static class McpErrorCodes
{
    public const string InvalidArgs = "INVALID_ARGS";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string UpstreamError = "UPSTREAM_ERROR";
    public const string RateLimited = "RATE_LIMITED";
    public const string ResultTooLarge = "RESULT_TOO_LARGE";
    public const string ToolNotFound = "TOOL_NOT_FOUND";
}
