namespace Shop.Application.Caching;

/// <summary>M9: Redis configuration. Kill-switch via <c>Enabled</c> (no redeploy needed with config reload).</summary>
public sealed class RedisOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public bool Enabled { get; set; } = true;
    public int DefaultTtlSeconds { get; set; } = 90;
}
