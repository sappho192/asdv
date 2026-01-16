using System.Text.Json;

namespace Agent.Core.Tools;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    string InputSchema { get; }
    ToolPolicy Policy { get; }

    Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext context, CancellationToken ct = default);
}

public record ToolPolicy
{
    public bool RequiresApproval { get; init; }
    public bool IsReadOnly { get; init; }
    public RiskLevel Risk { get; init; } = RiskLevel.Low;
}

public enum RiskLevel { Low, Medium, High }
