using Shop.Application.Ai;
using Shop.Application.Interfaces;

namespace Shop.Infrastructure.Ai;

/// <summary>
/// A4: deterministic, network-free provider for CI and local tests. Scripted
/// responses are returned in order; with no script it echoes the last user
/// message, so a test that only needs a valid turn needs no setup.
/// </summary>
public sealed class FakeAiProvider : IAiProvider
{
    private readonly Queue<AiResponse> _scripted = new();
    private readonly List<AiRequest> _requests = [];
    private readonly object _gate = new();

    /// <summary>Optional stateless responder, for a test that needs a computed reply.</summary>
    public Func<AiRequest, AiResponse>? Responder { get; set; }

    /// <summary>Every request seen, in call order.</summary>
    public IReadOnlyList<AiRequest> Requests
    {
        get
        {
            lock (_gate) return _requests.ToArray();
        }
    }

    public FakeAiProvider Enqueue(params AiResponse[] responses)
    {
        lock (_gate)
        {
            foreach (var response in responses)
                _scripted.Enqueue(response);
        }
        return this;
    }

    public FakeAiProvider RespondWith(Func<AiRequest, AiResponse> responder)
    {
        Responder = responder;
        return this;
    }

    public Task<AiResponse> GenerateAsync(AiRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        AiResponse? scripted = null;
        Func<AiRequest, AiResponse>? responder;
        lock (_gate)
        {
            _requests.Add(request);
            if (_scripted.Count > 0)
                scripted = _scripted.Dequeue();
            responder = Responder;
        }

        return Task.FromResult(scripted ?? responder?.Invoke(request) ?? Echo(request));
    }

    private static AiResponse Echo(AiRequest request)
    {
        var text = request.Messages.OfType<AiUserMessage>().LastOrDefault()?.Content ?? "fake-response";
        return new AiResponse(text, [], new AiUsage(0, 0), AiProviderPresets.Fake, TimeSpan.Zero, AiFinishReason.Stop);
    }
}
