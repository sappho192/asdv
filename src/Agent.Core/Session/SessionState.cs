namespace Agent.Core.Session;

/// <summary>
/// Mutable session state shared between orchestrator, CLI, and server.
/// Thread safety: int/long fields are atomic reads on .NET. For collection reads
/// from other threads (e.g., /status API), use ToArray() snapshot on RecentFilesTouched.
/// </summary>
public sealed class SessionState
{
    public string SessionId { get; init; } = null!;
    public string ProviderName { get; set; } = null!;
    public string ModelName { get; set; } = null!;
    public int IterationCount { get; set; }
    public int MessageCount { get; set; }
    public long EstimatedInputTokens { get; set; }
    public long EstimatedOutputTokens { get; set; }
    public int? MaxContextTokens { get; set; }
    public string? LastToolName { get; set; }
    public HashSet<string> RecentFilesTouched { get; } = new();
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CurrentModeName { get; set; }
    public Dictionary<string, string> Notes { get; } = new();
}
