using Agent.Core.Messages;
using Agent.Server.Models;
using Agent.Server.Services;
using FluentAssertions;

namespace Agent.Server.Tests;

public class SessionRuntimeFactoryTests
{
    [Fact]
    public void Create_RequiresWorkspacePath()
    {
        var factory = new SessionRuntimeFactory();
        var request = new CreateSessionRequest
        {
            WorkspacePath = "",
            Provider = "openai",
            Model = "gpt-5-mini"
        };

        var act = () => factory.Create(request);

        act.Should().Throw<ArgumentException>()
            .WithMessage("WorkspacePath is required.");
    }

    [Fact]
    public void Create_RejectsUnknownProvider()
    {
        var factory = new SessionRuntimeFactory();
        var workspace = CreateTempWorkspace();
        try
        {
            var request = new CreateSessionRequest
            {
                WorkspacePath = workspace,
                Provider = "unknown",
                Model = ""
            };

            var act = () => factory.Create(request);

            act.Should().Throw<ArgumentException>()
                .WithMessage("Unknown provider: unknown");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public async Task Create_UsesDefaultModelWhenNotProvided()
    {
        var factory = new SessionRuntimeFactory();
        var workspace = CreateTempWorkspace();
        var previousKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        try
        {
            var request = new CreateSessionRequest
            {
                WorkspacePath = workspace,
                Provider = "openai",
                Model = ""
            };

            var session = factory.Create(request);

            session.Info.Model.Should().Be("gpt-5-mini");
            await session.Logger.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previousKey);
            Directory.Delete(workspace, true);
        }
    }

    [Fact]
    public async Task CreateResume_UsesProvidedSessionIdAndMessages()
    {
        var factory = new SessionRuntimeFactory();
        var workspace = CreateTempWorkspace();
        var previousKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        try
        {
            var messages = new List<ChatMessage>
            {
                new UserMessage("hello")
            };
            var request = new ResumeSessionRequest
            {
                WorkspacePath = workspace,
                Provider = "openai",
                Model = "gpt-5-mini"
            };

            var session = factory.CreateResume("resume-1", request, messages);

            session.Info.Id.Should().Be("resume-1");
            session.Messages.Should().HaveCount(1);
            await session.Logger.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previousKey);
            Directory.Delete(workspace, true);
        }
    }

    private static string CreateTempWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agent_server_tests_{Guid.NewGuid():n}");
        Directory.CreateDirectory(path);
        return path;
    }
}
