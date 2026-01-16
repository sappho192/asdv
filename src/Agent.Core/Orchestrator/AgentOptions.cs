using Agent.Core.Workspace;

namespace Agent.Core.Orchestrator;

public record AgentOptions
{
    public required string RepoRoot { get; init; }
    public required string Model { get; init; }
    public required IWorkspace Workspace { get; init; }
    public string? SystemPrompt { get; init; }
    public int MaxIterations { get; init; } = 20;
    public int? MaxTokens { get; init; } = 4096;
    public double? Temperature { get; init; }
}
