using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shop.Application.Mcp;

namespace Shop.Mcp.Tooling;

/// <summary>
/// A1 / ADR-0006: filter the SDK-registered tool collection through the risk
/// policy and the per-tool kill switches. NEVER and disabled tools are removed
/// before the server announces its manifest.
///
/// The same pass adds a strict argument filter: unknown fields are rejected
/// with the standard INVALID_ARGS envelope instead of being silently ignored.
/// </summary>
public sealed class ToolManifestPostConfigure(
    IToolRiskPolicy policy,
    IConfiguration configuration,
    ILogger<ToolManifestPostConfigure> logger) : IPostConfigureOptions<McpServerOptions>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void PostConfigure(string? name, McpServerOptions options)
    {
        options.ScopeRequests = true;

        if (options.ToolCollection is null) return;

        var allowed = ToolManifest.Build(policy, IsEnabled)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removed = options.ToolCollection
            .Where(tool => !allowed.Contains(tool.ProtocolTool.Name))
            .ToList();

        foreach (var tool in removed)
            options.ToolCollection.Remove(tool);

        if (removed.Count > 0)
            logger.LogInformation(
                "MCP manifest filtered {Removed} tool(s) outside the risk policy", removed.Count);

        var schemas = options.ToolCollection
            .ToDictionary(
                tool => tool.ProtocolTool.Name,
                tool => tool.ProtocolTool.InputSchema
                    .GetProperty("properties")
                    .EnumerateObject()
                    .Select(p => p.Name)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);

        options.Filters.Request.CallToolFilters.Add(next => async (context, ct) =>
        {
            if (schemas.TryGetValue(context.Params.Name, out var properties)
                && context.Params.Arguments is { } arguments)
            {
                var unknown = arguments.Keys.Where(key => !properties.Contains(key)).ToList();
                if (unknown.Count > 0)
                {
                    var toolContext = context.Services?.GetService(typeof(McpToolContext)) as McpToolContext;
                    var error = new McpToolError(
                        McpErrorCodes.InvalidArgs,
                        $"Unknown argument(s): {string.Join(", ", unknown)}.",
                        toolContext?.CorrelationId ?? Guid.NewGuid().ToString("N"),
                        Retryable: false);
                    var element = JsonSerializer.SerializeToElement(error, Json);

                    return new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = element.GetRawText() }],
                        StructuredContent = element,
                        IsError = true,
                    };
                }
            }

            return await next(context, ct);
        });
    }

    private bool IsEnabled(string toolName) =>
        configuration.GetValue($"Tools:{toolName}:Enabled", true);
}
