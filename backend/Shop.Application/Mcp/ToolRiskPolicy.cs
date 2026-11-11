namespace Shop.Application.Mcp;

/// <summary>
/// A1 / ADR-0006: the initial A-track surface. Payment tools are documented as
/// <see cref="ToolRiskClass.Never"/> so the manifest generator can prove they
/// are absent rather than merely denied.
/// </summary>
public sealed class ToolRiskPolicy : IToolRiskPolicy
{
    public static readonly ToolRiskPolicy Default = new();

    private static readonly IReadOnlyList<ToolPolicy> Policies =
    [
        new("search_products", ToolRiskClass.Read, AllowAnonymous: true),
        new("get_product", ToolRiskClass.Read, AllowAnonymous: true),
        new("get_product_facets", ToolRiskClass.Read, AllowAnonymous: true),
        new("compare_products", ToolRiskClass.Read, AllowAnonymous: true),
        new("get_cart", ToolRiskClass.Read, AllowAnonymous: false),
        new("get_orders", ToolRiskClass.Read, AllowAnonymous: false),
        new("get_order", ToolRiskClass.Read, AllowAnonymous: false),
        new("get_order_status", ToolRiskClass.Read, AllowAnonymous: false),
        new("add_to_cart", ToolRiskClass.Write, AllowAnonymous: false),
        new("update_cart", ToolRiskClass.Write, AllowAnonymous: false),
        new("remove_from_cart", ToolRiskClass.Write, AllowAnonymous: false),
        new("create_quote", ToolRiskClass.Quote, AllowAnonymous: false),
        new("confirm_payment", ToolRiskClass.Never, AllowAnonymous: false),
        new("cancel_order", ToolRiskClass.Never, AllowAnonymous: false),
        new("refund_payment", ToolRiskClass.Never, AllowAnonymous: false),
    ];

    private static readonly IReadOnlyDictionary<string, ToolPolicy> ByName =
        Policies.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ToolPolicy> All => Policies;

    public ToolPolicy? Find(string name) =>
        ByName.TryGetValue(name, out var policy) ? policy : null;
}
