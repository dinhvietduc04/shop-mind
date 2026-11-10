using Shop.Application.Specs;

namespace Shop.UnitTests;

/// <summary>
/// A3 / ADR-0007: the per-category specs registry is the single source of truth.
/// If a key is not here, it does not exist for facets or filtered search.
/// </summary>
public class SpecRegistryTests
{
    [Fact]
    public void Registered_spec_exposes_its_wire_definition()
    {
        var ram = SpecRegistry.Find("ram_gb");

        Assert.NotNull(ram);
        Assert.Equal("ram_gb", ram!.Key);
        Assert.Equal("RAM (GB)", ram.Label);
        Assert.Equal(SpecType.Range, ram.Type);
        Assert.Equal("GB", ram.Unit);
    }

    [Fact]
    public void Unknown_spec_is_not_registered()
    {
        Assert.Null(SpecRegistry.Find("bogus_spec"));
        Assert.False(SpecRegistry.IsRegistered("bogus_spec"));
    }

    [Fact]
    public void Category_registry_only_returns_specs_declared_for_that_category()
    {
        var laptopKeys = SpecRegistry.ForCategory("laptops").Select(d => d.Key).ToHashSet();
        var headphoneKeys = SpecRegistry.ForCategory("headphones").Select(d => d.Key).ToHashSet();

        Assert.Contains("ram_gb", laptopKeys);
        Assert.Contains("cpu", laptopKeys);
        Assert.DoesNotContain("anc", laptopKeys);

        Assert.Contains("anc", headphoneKeys);
        Assert.Contains("wireless", headphoneKeys);
        Assert.DoesNotContain("ram_gb", headphoneKeys);
    }

    [Fact]
    public void Every_spec_is_declared_once()
    {
        var duplicates = SpecRegistry.All
            .GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }
}
