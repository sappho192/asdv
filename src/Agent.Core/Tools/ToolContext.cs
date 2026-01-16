using Agent.Core.Approval;
using Agent.Core.Workspace;

namespace Agent.Core.Tools;

public record ToolContext
{
    public required string RepoRoot { get; init; }
    public required IWorkspace Workspace { get; init; }
    public required IApprovalService ApprovalService { get; init; }
}
