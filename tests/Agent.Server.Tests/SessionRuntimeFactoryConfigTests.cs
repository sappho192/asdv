using Agent.Server.Models;
using Agent.Server.Services;
using FluentAssertions;

namespace Agent.Server.Tests;

public class SessionRuntimeFactoryConfigTests
{
    [Fact]
    public async Task Create_UsesYamlConfigForProviderAndModel()
    {
        var factory = new SessionRuntimeFactory();
        var workspace = CreateTempWorkspace();
        WriteConfig(workspace, """
            provider: openai-compatible
            model: local-model
            openaiCompatibleEndpoint: http://127.0.0.1:8080
            """);
        try
        {
            var request = new CreateSessionRequest
            {
                WorkspacePath = workspace,
                Provider = "",
                Model = ""
            };

            var session = factory.Create(request);

            session.Info.Provider.Should().Be("openai-compatible");
            session.Info.Model.Should().Be("local-model");
            await session.Logger.DisposeAsync();
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public void Create_ThrowsWhenOpenAICompatibleModelMissing()
    {
        var factory = new SessionRuntimeFactory();
        var workspace = CreateTempWorkspace();
        WriteConfig(workspace, """
            provider: openai-compatible
            openaiCompatibleEndpoint: http://127.0.0.1:8080
            """);
        try
        {
            var request = new CreateSessionRequest
            {
                WorkspacePath = workspace,
                Provider = "",
                Model = ""
            };

            var act = () => factory.Create(request);

            act.Should().Throw<ArgumentException>()
                .WithMessage("Model is required for openai-compatible provider. Provide Model in the request or asdv.yaml.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public void Create_ThrowsWhenOpenAICompatibleEndpointMissing()
    {
        var factory = new SessionRuntimeFactory();
        var workspace = CreateTempWorkspace();
        WriteConfig(workspace, """
            provider: openai-compatible
            model: local-model
            """);
        try
        {
            var request = new CreateSessionRequest
            {
                WorkspacePath = workspace,
                Provider = "openai-compatible",
                Model = "local-model"
            };

            var act = () => factory.Create(request);

            act.Should().Throw<ArgumentException>()
                .WithMessage("OpenAI-compatible endpoint is required. Set openaiCompatibleEndpoint in asdv.yaml.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    private static string CreateTempWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agent_server_config_{Guid.NewGuid():n}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteConfig(string workspace, string yaml)
    {
        var path = Path.Combine(workspace, "asdv.yaml");
        File.WriteAllText(path, yaml);
    }
}
