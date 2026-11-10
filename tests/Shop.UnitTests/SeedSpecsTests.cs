using System.Text.Json;
using Shop.Application.Specs;
using Shop.Infrastructure.Seed;

namespace Shop.UnitTests;

/// <summary>
/// A3 / ADR-0007: seed data is validated against the same registry that drives
/// facets and filtered search. Drift fails CI, not a demo.
/// </summary>
public class SeedSpecsTests
{
    [Fact]
    public void Seed_catalog_specs_are_registered_and_type_correct()
    {
        SeedCatalog.Validate();
    }

    [Fact]
    public void Registry_rejects_values_of_the_wrong_type()
    {
        var ram = SpecRegistry.Find("ram_gb")!;

        Assert.True(SpecRegistry.IsValidValue(ram, JsonSerializer.SerializeToElement(16)));
        Assert.False(SpecRegistry.IsValidValue(ram, JsonSerializer.SerializeToElement("16")));
        Assert.False(SpecRegistry.IsValidValue(ram, JsonSerializer.SerializeToElement(true)));

        var cpu = SpecRegistry.Find("cpu")!;
        Assert.True(SpecRegistry.IsValidValue(cpu, JsonSerializer.SerializeToElement("Intel Core i7")));
        Assert.False(SpecRegistry.IsValidValue(cpu, JsonSerializer.SerializeToElement(7)));

        var anc = SpecRegistry.Find("anc")!;
        Assert.True(SpecRegistry.IsValidValue(anc, JsonSerializer.SerializeToElement(true)));
        Assert.False(SpecRegistry.IsValidValue(anc, JsonSerializer.SerializeToElement("true")));
    }
}
