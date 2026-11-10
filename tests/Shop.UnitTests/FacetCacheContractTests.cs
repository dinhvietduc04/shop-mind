using System.Text.Json;
using Shop.Application.Dtos;

namespace Shop.UnitTests;

/// <summary>
/// A3: facets are cached through the M9 JSON contract. If the interface-typed
/// result cannot round-trip, every HIT would be a silent MISS.
/// </summary>
public class FacetCacheContractTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Facet_list_round_trips_through_the_cache_json_contract()
    {
        IReadOnlyList<FacetDto> facets =
        [
            new("ram_gb", "RAM (GB)", "range", "GB", null, 8, 32, 3),
            new("panel", "Panel", "enum", null, ["IPS", "VA"], null, null, 2),
        ];

        var json = JsonSerializer.Serialize(facets, Json);
        var back = JsonSerializer.Deserialize<IReadOnlyList<FacetDto>>(json, Json);

        Assert.NotNull(back);
        Assert.Equal(2, back!.Count);
        Assert.Equal("ram_gb", back[0].Key);
        Assert.Equal(8, back[0].Min);
        Assert.Equal(["IPS", "VA"], back[1].Values);
    }
}
