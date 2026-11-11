using System.Text.Json;

namespace Shop.Application.Specs;

/// <summary>
/// A3 / ADR-0007: the per-category Specs registry — the one authority for
/// facets, filtered-search validation, and seed-catalog CI validation. The
/// vocabulary mirrors the client-side registry in <c>fe/src/lib/attributes</c>
/// so F8 conformance is a check, not a redesign.
/// </summary>
public static class SpecRegistry
{
    private static readonly IReadOnlyList<SpecDefinition> Definitions =
    [
        new("ram_gb", "RAM (GB)", SpecType.Range, "GB", ["laptops", "smartphones"]),
        new("storage_gb", "Storage (GB)", SpecType.Range, "GB", ["laptops", "smartphones"]),
        new("screen_in", "Screen (in)", SpecType.Range, "in", ["laptops", "smartphones", "monitors"]),
        new("cpu", "CPU", SpecType.Enum, null, ["laptops", "smartphones"]),
        new("weight_kg", "Weight (kg)", SpecType.Range, "kg", ["laptops"]),
        new("thunderbolt", "Thunderbolt", SpecType.Bool, null, ["laptops"]),
        new("camera_mp", "Camera (MP)", SpecType.Range, "MP", ["smartphones"]),
        new("s_pen", "S Pen", SpecType.Bool, null, ["smartphones"]),
        new("resolution", "Resolution", SpecType.Enum, null, ["monitors"]),
        new("usb_c", "USB-C", SpecType.Bool, null, ["monitors"]),
        new("panel", "Panel", SpecType.Enum, null, ["monitors"]),
        new("wireless", "Wireless", SpecType.Bool, null, ["keyboards", "mice", "headphones"]),
        new("backlit", "Backlit", SpecType.Bool, null, ["keyboards"]),
        new("layout", "Layout", SpecType.Enum, null, ["keyboards"]),
        new("dpi_max", "Max DPI", SpecType.Range, "dpi", ["mice"]),
        new("buttons", "Buttons", SpecType.Range, null, ["mice"]),
        new("anc", "Noise cancelling", SpecType.Bool, null, ["headphones"]),
        new("battery_h", "Battery (h)", SpecType.Range, "h", ["headphones"]),
        new("vram_gb", "VRAM (GB)", SpecType.Range, "GB", ["components"]),
        new("interface", "Interface", SpecType.Enum, null, ["components"]),
        new("tdp_w", "TDP (W)", SpecType.Range, "W", ["components"]),
        new("ports", "Ports", SpecType.Range, null, ["accessories"]),
        new("4k_hdmi", "4K HDMI", SpecType.Bool, null, ["accessories"]),
        new("pd_w", "Power delivery (W)", SpecType.Range, "W", ["accessories"]),
    ];

    private static readonly IReadOnlyDictionary<string, SpecDefinition> ByKey =
        Definitions.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SpecDefinition> All => Definitions;

    public static SpecDefinition? Find(string key) =>
        ByKey.TryGetValue(key, out var def) ? def : null;

    public static bool IsRegistered(string key) => ByKey.ContainsKey(key);

    /// <summary>The registry slice for one category slug (empty for an unknown category).</summary>
    public static IReadOnlyList<SpecDefinition> ForCategory(string categorySlug) =>
        Definitions
            .Where(d => d.Categories.Contains(categorySlug, StringComparer.OrdinalIgnoreCase))
            .ToList();

    /// <summary>Seed/admin validation: does this JSON value match the registry type?</summary>
    public static bool IsValidValue(SpecDefinition def, JsonElement value) => def.Type switch
    {
        SpecType.Range => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out _),
        SpecType.Enum => value.ValueKind == JsonValueKind.String
                         && !string.IsNullOrWhiteSpace(value.GetString()),
        SpecType.Bool => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        _ => false,
    };
}
