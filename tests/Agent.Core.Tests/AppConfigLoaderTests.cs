using Agent.Core.Config;
using FluentAssertions;

namespace Agent.Core.Tests;

public class AppConfigLoaderTests
{
    [Fact]
    public void LoadIfExists_ReturnsNull_WhenFileMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():n}.yaml");

        var config = AppConfigLoader.LoadIfExists(path);

        config.Should().BeNull();
    }

    [Fact]
    public void Load_ReturnsEmptyConfig_WhenRootIsNotMapping()
    {
        var path = CreateTempConfig("scalar-value");
        try
        {
            var config = AppConfigLoader.Load(path);

            config.Provider.Should().BeNull();
            config.Model.Should().BeNull();
            config.OpenAICompatibleEndpoint.Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("openaiCompatibleEndpoint")]
    [InlineData("openai_compatible_endpoint")]
    [InlineData("openai-compatible-endpoint")]
    public void Load_ReadsOpenAICompatibleEndpointAliases(string key)
    {
        var yaml = $"""
            provider: openai-compatible
            model: local-model
            {key}: " http://localhost:8080 "
            """;
        var path = CreateTempConfig(yaml);
        try
        {
            var config = AppConfigLoader.Load(path);

            config.Provider.Should().Be("openai-compatible");
            config.Model.Should().Be("local-model");
            config.OpenAICompatibleEndpoint.Should().Be("http://localhost:8080");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_TreatsEmptyScalarValuesAsNull()
    {
        var yaml = """
            provider: "   "
            model: ""
            openaiCompatibleEndpoint: "   "
            """;
        var path = CreateTempConfig(yaml);
        try
        {
            var config = AppConfigLoader.Load(path);

            config.Provider.Should().BeNull();
            config.Model.Should().BeNull();
            config.OpenAICompatibleEndpoint.Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempConfig(string yaml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"app_config_{Guid.NewGuid():n}.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }
}
