using Agent.Core.Modes;
using Agent.Core.Session;
using Agent.Core.Workflows;

namespace Agent.Cli.Commands;

public interface ICommand
{
    string Name { get; }
    string Description { get; }
    Task ExecuteAsync(string[] args, CommandContext context);
}

public record CommandContext
{
    public required string ProviderName { get; init; }
    public required string ModelName { get; init; }
    public required string SessionId { get; init; }
    public required string SessionPath { get; init; }
    public required string RepoRoot { get; init; }
    public required bool AutoApprove { get; init; }
    public SessionState? State { get; init; }
    public Action<string>? OnModelChanged { get; init; }
    public Func<bool>? OnApproveAllToggled { get; init; }
    public Func<bool>? GetAutoApproveState { get; init; }
    public Action<IExecutionMode?>? OnModeChanged { get; init; }
    public Action<WorkflowManifest, string?>? OnWorkflowRequested { get; init; }

    public bool GetLiveAutoApprove() => GetAutoApproveState?.Invoke() ?? AutoApprove;
}
