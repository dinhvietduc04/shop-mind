using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Ai;
using Shop.Application.Interfaces;
using Shop.Domain.Exceptions;
using Shop.Infrastructure;
using Shop.Infrastructure.Ai;

namespace Shop.UnitTests.Ai;

/// <summary>
/// A4: the seam is only real if the container picks it and the config switch is
/// enough. Builds the real composition root without touching a database.
/// </summary>
public class AiProviderCompositionTests
{
    [Fact]
    public void Fake_provider_is_selected_by_config()
    {
        using var services = Services(("Ai:Provider", "fake"));

        Assert.IsType<FakeAiProvider>(services.GetRequiredService<IAiProvider>());
    }

    [Fact]
    public async Task Missing_key_does_not_break_resolution()
    {
        using var services = Services(("Ai:Provider", "opencode-go"));

        var provider = services.GetRequiredService<IAiProvider>();

        Assert.IsType<OpenAiCompatibleProvider>(provider);
        var ex = await Assert.ThrowsAsync<AiProviderException>(() => provider.GenerateAsync(Request()));
        Assert.Equal(AiProviderException.NotConfiguredCode, ex.Code);
    }

    [Fact]
    public async Task Tool_call_smoke_runs_through_the_provider_seam()
    {
        using var services = Services(("Ai:Provider", "fake"));
        var provider = services.GetRequiredService<IAiProvider>();
        var fake = Assert.IsType<FakeAiProvider>(provider);
        fake.Enqueue(new AiResponse(
            null,
            [new AiToolCall("call_1", "search_products", """{"query":"laptop"}""")],
            new AiUsage(3, 5),
            "fake",
            TimeSpan.Zero,
            AiFinishReason.ToolCalls));

        var response = await provider.GenerateAsync(new AiRequest(
            [new AiUserMessage("find a laptop")],
            [new AiToolDefinition("search_products", "Search the catalog", """{"type":"object"}""")]));

        Assert.Equal(AiFinishReason.ToolCalls, response.FinishReason);
        Assert.Equal("search_products", Assert.Single(response.ToolCalls).Name);
        Assert.Single(fake.Requests);
    }

    private static ServiceProvider Services(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static AiRequest Request() => new([new AiUserMessage("hello")]);
}
