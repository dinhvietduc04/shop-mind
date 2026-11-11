using Shop.Application.Ai;
using Shop.Infrastructure.Ai;

namespace Shop.UnitTests.Ai;

/// <summary>
/// A4: the fake is the CI provider, so its determinism is part of the contract.
/// </summary>
public class FakeAiProviderTests
{
    [Fact]
    public async Task Echoes_the_last_user_message_when_no_script_is_queued()
    {
        var provider = new FakeAiProvider();

        var response = await provider.GenerateAsync(Request("find a laptop"));

        Assert.Equal("find a laptop", response.Text);
        Assert.Empty(response.ToolCalls);
        Assert.Equal(AiFinishReason.Stop, response.FinishReason);
        Assert.Single(provider.Requests);
    }

    [Fact]
    public async Task Scripted_responses_are_returned_in_order()
    {
        var provider = new FakeAiProvider().Enqueue(
            Text("first"),
            new AiResponse(null, [new AiToolCall("call_1", "search_products", "{}")],
                new AiUsage(1, 2), "fake", TimeSpan.Zero, AiFinishReason.ToolCalls),
            Text("third"));

        Assert.Equal("first", (await provider.GenerateAsync(Request("one"))).Text);
        Assert.Equal(AiFinishReason.ToolCalls, (await provider.GenerateAsync(Request("two"))).FinishReason);
        Assert.Equal("third", (await provider.GenerateAsync(Request("three"))).Text);
        Assert.Equal(3, provider.Requests.Count);
    }

    [Fact]
    public async Task Responder_computes_a_reply_when_no_script_is_queued()
    {
        var provider = new FakeAiProvider().RespondWith(request =>
            Text($"echo:{((AiUserMessage)request.Messages[0]).Content}"));

        var response = await provider.GenerateAsync(Request("hello"));

        Assert.Equal("echo:hello", response.Text);
    }

    private static AiRequest Request(string message) => new([new AiUserMessage(message)]);

    private static AiResponse Text(string text) =>
        new(text, [], new AiUsage(0, 0), AiProviderPresets.Fake, TimeSpan.Zero, AiFinishReason.Stop);
}
