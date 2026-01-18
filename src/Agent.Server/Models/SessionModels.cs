namespace Agent.Server.Models;

public sealed record CreateSessionRequest
{
    public string WorkspacePath { get; init; } = "";
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
}

public sealed record CreateSessionResponse(string SessionId);

public sealed record ChatRequest
{
    public string Message { get; init; } = "";
}

public sealed record ApprovalRequest
{
    public bool Approved { get; init; }
}

public sealed record SessionInfo(
    string Id,
    string WorkspacePath,
    string Provider,
    string Model,
    DateTimeOffset CreatedAt);
