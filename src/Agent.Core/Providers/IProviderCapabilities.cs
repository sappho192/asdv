namespace Agent.Core.Providers;

public interface IProviderCapabilities
{
    int? MaxContextTokens { get; }
    int? MaxOutputTokens { get; }
}
