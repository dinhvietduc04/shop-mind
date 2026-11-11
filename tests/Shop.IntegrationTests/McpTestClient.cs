using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace Shop.IntegrationTests;

/// <summary>A1: connects an MCP client to the in-process Streamable HTTP endpoint.</summary>
public static class McpTestClient
{
    public static async Task<McpClient> ConnectAsync(McpWebFactory factory, string? token = null)
    {
        var http = factory.CreateClient();
        if (token is not null)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            http,
            NullLoggerFactory.Instance,
            false);

        return await McpClient.CreateAsync(transport);
    }
}
