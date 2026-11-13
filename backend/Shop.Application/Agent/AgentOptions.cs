namespace Shop.Application.Agent;

/// <summary>
/// A5/A6: the loop budgets and kill switch from the plan's config sketch.
/// Values are read through <c>IOptionsMonitor</c> so a reload applies to the
/// next turn; the per-conversation turn cap is additionally persisted on the
/// conversation, so changing this setting never rewrites an old thread's budget.
/// </summary>
public sealed class AgentOptions
{
    private const int DefaultMaxToolCallsPerTurn = 10;
    private const int DefaultMaxTurnSeconds = 30;
    private const int DefaultMaxRetriesPerTool = 2;
    private const int DefaultHistoryTurns = 10;
    private const int DefaultMaxPromptTokens = 8000;
    private const int DefaultMaxTurnsPerConversation = 20;
    private const int DefaultAgentTokenTtlSeconds = 60;
    private const int DefaultMaxMessageChars = 2000;
    private const int DefaultMaxOutputTokens = 1024;
    private const double DefaultTemperature = 0.2;

    public bool Enabled { get; set; } = true;

    /// <summary>Assist is the only supported level in v1; Delegated is a seam.</summary>
    public string TrustLevel { get; set; } = "Assist";

    public int MaxToolCallsPerTurn { get; set; } = DefaultMaxToolCallsPerTurn;
    public int MaxTurnSeconds { get; set; } = DefaultMaxTurnSeconds;

    /// <summary>Retries after the first attempt (so 2 = at most 3 attempts).</summary>
    public int MaxRetriesPerTool { get; set; } = DefaultMaxRetriesPerTool;

    /// <summary>Whole-turn history window (system prompt is always kept).</summary>
    public int HistoryTurns { get; set; } = DefaultHistoryTurns;

    /// <summary>Trim whole turns from the oldest end until the prompt fits.</summary>
    public int MaxPromptTokens { get; set; } = DefaultMaxPromptTokens;

    /// <summary>Server-enforced per-conversation turn budget surfaced as turnCap.</summary>
    public int MaxTurnsPerConversation { get; set; } = DefaultMaxTurnsPerConversation;

    /// <summary>The minted scope=agent token lives for one turn (hard cap 60s).</summary>
    public int AgentTokenTtlSeconds { get; set; } = DefaultAgentTokenTtlSeconds;

    public int MaxMessageChars { get; set; } = DefaultMaxMessageChars;
    public int MaxOutputTokens { get; set; } = DefaultMaxOutputTokens;
    public double Temperature { get; set; } = DefaultTemperature;

    /// <summary>Shop.Mcp Streamable HTTP endpoint used by the agent tool client.</summary>
    public string McpEndpoint { get; set; } = "http://localhost:5001/mcp";
}
