namespace Agent.Core.Workflows;

public sealed record WorkflowManifest
{
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public List<WorkflowStep> Steps { get; init; } = new();
}

public sealed record WorkflowStep
{
    public string Mode { get; init; } = null!;
    public string? Prompt { get; init; }
    public int? MaxIterations { get; init; }
}
