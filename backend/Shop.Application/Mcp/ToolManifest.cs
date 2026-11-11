namespace Shop.Application.Mcp;

/// <summary>
/// A1 / ADR-0006: the manifest is generated from the risk policy plus the
/// per-tool kill switches. NEVER and disabled tools are dropped here, before
/// the MCP SDK ever sees them.
/// </summary>
public static class ToolManifest
{
    public static IReadOnlyList<ToolPolicy> Build(
        IToolRiskPolicy policy,
        Func<string, bool> isEnabled)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(isEnabled);

        return policy.All
            .Where(p => p.RiskClass != ToolRiskClass.Never)
            .Where(p => isEnabled(p.Name))
            .ToList();
    }
}
