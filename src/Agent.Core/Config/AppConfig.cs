namespace Agent.Core.Config;

public sealed record AppConfig
{
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? OpenAICompatibleEndpoint { get; init; }
    public int? MaxContextTokens { get; init; }
    public string? SummaryModel { get; init; }
}
