using Shop.Application.Mcp;

namespace Shop.UnitTests.Mcp;

/// <summary>
/// A1 / ADR-0006: the risk policy is the single tool-to-risk mapping. The MCP
/// manifest is generated from it, so a NEVER or disabled tool is absent rather
/// than merely denied.
/// </summary>
public class ToolRiskPolicyTests
{
    private static readonly IToolRiskPolicy Policy = ToolRiskPolicy.Default;

    [Fact]
    public void A1_tools_have_the_expected_risk_and_anonymous_rules()
    {
        AssertPolicy("search_products", ToolRiskClass.Read, anonymous: true);
        AssertPolicy("get_product", ToolRiskClass.Read, anonymous: true);
        AssertPolicy("get_product_facets", ToolRiskClass.Read, anonymous: true);
        AssertPolicy("get_cart", ToolRiskClass.Read, anonymous: false);
    }

    [Fact]
    public void A2_tools_have_the_expected_risk_and_anonymous_rules()
    {
        AssertPolicy("compare_products", ToolRiskClass.Read, anonymous: true);
        AssertPolicy("get_orders", ToolRiskClass.Read, anonymous: false);
        AssertPolicy("get_order", ToolRiskClass.Read, anonymous: false);
        AssertPolicy("get_order_status", ToolRiskClass.Read, anonymous: false);
        AssertPolicy("add_to_cart", ToolRiskClass.Write, anonymous: false);
        AssertPolicy("update_cart", ToolRiskClass.Write, anonymous: false);
        AssertPolicy("remove_from_cart", ToolRiskClass.Write, anonymous: false);
        AssertPolicy("create_quote", ToolRiskClass.Quote, anonymous: false);
    }

    [Fact]
    public void M13_tools_are_read_only_and_only_the_discovery_tool_is_anonymous()
    {
        AssertPolicy("find_products_for_requirement", ToolRiskClass.Read, anonymous: true);
        AssertPolicy("prepare_checkout", ToolRiskClass.Read, anonymous: false);
    }

    [Fact]
    public void Manifest_contains_every_live_tool_and_no_never_tool()
    {
        var manifest = ToolManifest.Build(Policy, _ => true);

        Assert.Equal(14, manifest.Count);
        Assert.All(manifest, policy => Assert.NotEqual(ToolRiskClass.Never, policy.RiskClass));
    }

    [Fact]
    public void Human_only_money_and_cancel_tools_are_documented_as_never()
    {
        AssertPolicy("cancel_order", ToolRiskClass.Never, anonymous: false);
        AssertPolicy("refund_payment", ToolRiskClass.Never, anonymous: false);

        var manifest = ToolManifest.Build(Policy, _ => true);

        Assert.DoesNotContain(manifest, p => p.Name is "cancel_order" or "refund_payment");
    }

    [Fact]
    public void Payment_tools_are_documented_as_never_and_never_enter_the_manifest()
    {
        AssertPolicy("confirm_payment", ToolRiskClass.Never, anonymous: false);

        var manifest = ToolManifest.Build(Policy, _ => true);

        Assert.DoesNotContain(manifest, p => p.Name == "confirm_payment");
    }

    [Fact]
    public void Manifest_excludes_disabled_tools()
    {
        var manifest = ToolManifest.Build(Policy, name => name != "get_product");

        Assert.Contains(manifest, p => p.Name == "search_products");
        Assert.DoesNotContain(manifest, p => p.Name == "get_product");
    }

    [Fact]
    public void Policy_names_are_unique_and_non_empty()
    {
        Assert.All(Policy.All, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));

        var duplicates = Policy.All
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Unknown_tool_has_no_policy_entry()
    {
        Assert.Null(Policy.Find("not_a_tool"));
    }

    private static void AssertPolicy(string name, ToolRiskClass risk, bool anonymous)
    {
        var policy = Policy.Find(name);

        Assert.NotNull(policy);
        Assert.Equal(risk, policy!.RiskClass);
        Assert.Equal(anonymous, policy.AllowAnonymous);
    }
}
