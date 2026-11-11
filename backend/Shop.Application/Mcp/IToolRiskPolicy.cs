namespace Shop.Application.Mcp;

/// <summary>
/// A1 / ADR-0006: single source of truth for the tool-to-risk mapping. The MCP
/// manifest is generated from this, never hand-written.
/// </summary>
public interface IToolRiskPolicy
{
    IReadOnlyList<ToolPolicy> All { get; }

    ToolPolicy? Find(string name);
}
