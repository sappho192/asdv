using Agent.Core.Logging;
using Agent.Core.Messages;
using Agent.Core.Orchestrator;
using Agent.Core.Policy;
using Agent.Core.Providers;
using Agent.Core.Tools;
using Agent.Logging;
using Agent.Llm.Anthropic;
using Agent.Llm.OpenAI;
using Agent.Server.Models;
using Agent.Tools;
using Agent.Workspace;

namespace Agent.Server.Services;

public sealed class SessionRuntimeFactory
{
    public SessionRuntime Create(CreateSessionRequest request)
    {
        return CreateInternal(request, null, null);
    }

    public SessionRuntime CreateResume(string sessionId, ResumeSessionRequest request, List<ChatMessage> messages)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("SessionId is required.");
        }

        var createRequest = new CreateSessionRequest
        {
            WorkspacePath = request.WorkspacePath,
            Provider = request.Provider,
            Model = request.Model
        };

        return CreateInternal(createRequest, sessionId.Trim(), messages);
    }

    public static string GetSessionLogPath(string repoRoot, string sessionId)
    {
        return Path.Combine(repoRoot, ".agent", $"session_{sessionId}.jsonl");
    }

    private SessionRuntime CreateInternal(
        CreateSessionRequest request,
        string? sessionId,
        List<ChatMessage>? messages)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspacePath))
        {
            throw new ArgumentException("WorkspacePath is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            throw new ArgumentException("Provider is required.");
        }

        var repoRoot = Path.GetFullPath(request.WorkspacePath);
        if (!Directory.Exists(repoRoot))
        {
            throw new ArgumentException($"Workspace path does not exist: {repoRoot}");
        }

        var provider = request.Provider.Trim().ToLowerInvariant();
        var model = string.IsNullOrWhiteSpace(request.Model)
            ? GetDefaultModel(provider)
            : request.Model.Trim();

        sessionId ??= Guid.NewGuid().ToString("n");
        var info = new SessionInfo(
            sessionId,
            repoRoot,
            provider,
            model,
            DateTimeOffset.UtcNow);

        var workspace = new LocalWorkspace(repoRoot);
        var toolRegistry = CreateToolRegistry();
        var modelProvider = CreateProvider(provider);
        var policyEngine = new DefaultPolicyEngine(new PolicyOptions { AutoApprove = false });

        var logger = CreateLogger(repoRoot, sessionId);
        var approvalService = new ServerApprovalService();

        var options = new AgentOptions
        {
            RepoRoot = repoRoot,
            Model = model,
            Workspace = workspace,
            MaxIterations = 20,
            MaxTokens = 4096,
            SystemPrompt = SystemPromptProvider.GetSystemPrompt()
        };

        var runtime = new SessionRuntime(
            info,
            options,
            toolRegistry,
            modelProvider,
            policyEngine,
            logger,
            approvalService);
        if (messages is { Count: > 0 })
        {
            runtime.Messages.AddRange(messages);
        }

        return runtime;
    }

    private static string GetDefaultModel(string provider)
    {
        return provider switch
        {
            "anthropic" => "claude-sonnet-4-20250514",
            "openai" => "gpt-5-mini",
            _ => throw new ArgumentException($"Unknown provider: {provider}")
        };
    }

    private static IModelProvider CreateProvider(string provider)
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        return provider switch
        {
            "anthropic" => new ClaudeProvider(
                httpClient,
                Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                    ?? throw new InvalidOperationException(
                        "ANTHROPIC_API_KEY environment variable is not set")),
            "openai" => new OpenAIProvider(
                httpClient,
                Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? throw new InvalidOperationException(
                        "OPENAI_API_KEY environment variable is not set"),
                Environment.GetEnvironmentVariable("OPENAI_BASE_URL")),
            _ => throw new ArgumentException($"Unknown provider: {provider}")
        };
    }

    private static ToolRegistry CreateToolRegistry()
    {
        var registry = new ToolRegistry();

        registry.Register(new ReadFileTool());
        registry.Register(new ListFilesTool());
        registry.Register(new SearchTextTool());
        registry.Register(new GitStatusTool());
        registry.Register(new GitDiffTool());
        registry.Register(new ApplyPatchTool());
        registry.Register(new RunCommandTool());

        return registry;
    }

    private static ISessionLogger CreateLogger(string repoRoot, string sessionId)
    {
        try
        {
            var sessionPath = GetSessionLogPath(repoRoot, sessionId);
            return new JsonlSessionLogger(sessionPath);
        }
        catch
        {
            return NullSessionLogger.Instance;
        }
    }
}
