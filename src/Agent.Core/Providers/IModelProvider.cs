using Agent.Core.Events;
using Agent.Core.Messages;

namespace Agent.Core.Providers;

public interface IModelProvider
{
    string Name { get; }

    IAsyncEnumerable<AgentEvent> StreamAsync(
        ModelRequest request,
        CancellationToken ct = default
    );
}

public record ModelRequest
{
    public required string Model { get; init; }
    public string? SystemPrompt { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public IReadOnlyList<ToolDefinition>? Tools { get; init; }
    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
}

public record ToolDefinition(string Name, string Description, string InputSchema);
