namespace Shop.Infrastructure.Ai;

/// <summary>
/// A4: AI settings. The key comes from the environment only (<c>Ai__ApiKey</c>);
/// <see cref="Model"/> and <see cref="BaseUrl"/> are overrides, so switching a
/// preset needs only <see cref="Provider"/> plus the key.
/// </summary>
public sealed class AiOptions
{
    public string Provider { get; set; } = AiProviderPresets.OpenCodeGo;
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public string? BaseUrl { get; set; }
    public string? SessionId { get; set; }
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxRetries { get; set; } = 2;
    public int RetryBaseDelayMs { get; set; } = 200;
}
