using Shop.Application.Specs;
using Shop.Domain.Exceptions;

namespace Shop.UnitTests;

/// <summary>
/// A3 / §B5+ 4: one fail-closed grammar between REST callers, the future MCP
/// tool, and the facets UI — <c>key:op:value</c>, clauses joined by <c>;</c>,
/// <c>in</c> values joined by <c>|</c>.
/// </summary>
public class SpecFilterCodecTests
{
    [Fact]
    public void Parses_range_filter_into_a_typed_clause()
    {
        var clauses = SpecFilterCodec.Parse("ram_gb:gte:16");

        var clause = Assert.Single(clauses);
        Assert.Equal("ram_gb", clause.Key);
        Assert.Equal(SpecOp.Gte, clause.Op);
        Assert.Equal(16d, clause.Value);
    }

    [Fact]
    public void Parses_enum_in_and_bool_clauses_with_the_right_value_types()
    {
        var clauses = SpecFilterCodec.Parse("panel:in:IPS|VA;wireless:eq:true");

        Assert.Equal(2, clauses.Count);
        Assert.Equal(SpecOp.In, clauses[0].Op);
        Assert.Equal(new[] { "IPS", "VA" }, Assert.IsAssignableFrom<IReadOnlyList<string>>(clauses[0].Value));
        Assert.Equal(SpecOp.Eq, clauses[1].Op);
        Assert.Equal(true, clauses[1].Value);
    }

    [Fact]
    public void Empty_filter_is_an_empty_clause_list()
    {
        Assert.Empty(SpecFilterCodec.Parse(null));
        Assert.Empty(SpecFilterCodec.Parse("  "));
    }

    [Theory]
    [InlineData("bogus:gte:1", "not a registered spec")]
    [InlineData("ram_gb>=16", "not key:op:value")]
    [InlineData("panel:gte:3", "not allowed")]
    [InlineData("ram_gb:in:8|16", "not allowed")]
    [InlineData("ram_gb:gte:lots", "needs a number")]
    [InlineData("wireless:eq:maybe", "needs true/false")]
    [InlineData("ram_gb:gte:", "has no value")]
    public void Rejects_malformed_or_unregistered_clauses(string raw, string expectedFragment)
    {
        var ex = Assert.Throws<InvalidSpecFilterException>(() => SpecFilterCodec.Parse(raw));

        Assert.Contains(expectedFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("InvalidSpecFilter", ex.Code);
    }
}
