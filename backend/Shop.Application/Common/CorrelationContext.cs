namespace Shop.Application.Common;

/// <summary>
/// M10: per-request correlation id (X-Correlation-Id middleware → logs → outbox
/// payload → future MCP tool trace). Flows with the async execution context.
/// </summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? Id => Current.Value;

    public static IDisposable Begin(string correlationId)
    {
        Current.Value = correlationId;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => Current.Value = null;
    }
}
