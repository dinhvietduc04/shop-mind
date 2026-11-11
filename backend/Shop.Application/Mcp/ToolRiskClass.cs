namespace Shop.Application.Mcp;

/// <summary>
/// A1 / ADR-0006: the risk class drives the MCP manifest and the server-side
/// budget. <see cref="Never"/> tools are deliberately absent from the surface.
/// </summary>
public enum ToolRiskClass
{
    Read,
    Write,
    Quote,
    Never,
}
