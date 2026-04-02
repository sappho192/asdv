using System.CommandLine;
using System.Text.Json;
using Agent.Cli;
using Agent.Cli.Commands;
using Agent.Cli.Rendering;
using Agent.Core.Config;
using Agent.Core.Approval;
using Agent.Core.Logging;
using Agent.Core.Orchestrator;
using Agent.Core.Policy;
using Agent.Core.Providers;
using Agent.Core.Session;
using Agent.Core.Tools;
using Agent.Llm.Anthropic;
using Agent.Llm.OpenAI;
using Agent.Logging;
using Agent.Tools;
using Agent.Workspace;
using DotNetEnv;

// Load .env file if it exists
Env.Load();

var repoOption = new Option<string>(
    aliases: ["--repo", "-r"],
    getDefaultValue: () => Environment.CurrentDirectory,
    description: "Repository root path");

var providerOption = new Option<string?>(
    aliases: ["--provider", "-p"],
    description: "LLM provider (openai|anthropic|openai-compatible)");

var modelOption = new Option<string?>(
    aliases: ["--model", "-m"],
    description: "Model name (default: provider-specific)");

var configOption = new Option<string?>(
    aliases: ["--config", "-c"],
    description: "Path to YAML config (default: asdv.yaml in repo root)");

var autoApproveOption = new Option<bool>(
    aliases: ["--yes", "-y"],
    getDefaultValue: () => false,
    description: "Auto-approve all tool calls (use with caution)");

var sessionOption = new Option<string?>(
    aliases: ["--session", "-s"],
    description: "Session log file path");

var sessionIdOption = new Option<string?>(
    aliases: ["--session-id", "--sid"],
    description: "Session ID for resume/new session (stored under .agent)");

var maxIterationsOption = new Option<int>(
    aliases: ["--max-iterations"],
    getDefaultValue: () => 20,
    description: "Maximum agent iterations");

var debugOption = new Option<bool>(
    aliases: ["--debug", "-d"],
    getDefaultValue: () => false,
    description: "Enable debug output (stack traces, detailed errors)");

var onceOption = new Option<bool>(
    aliases: ["--once"],
    getDefaultValue: () => false,
    description: "Run a single prompt and exit (non-interactive)");

var resumeModeOption = new Option<string>(
    aliases: ["--resume-mode"],
    getDefaultValue: () => "full",
    description: "Resume mode: full|summary|last-N (e.g., last-5)");

var promptArgument = new Argument<string?>(
    name: "prompt",
    description: "Task prompt for the agent")
{
    Arity = ArgumentArity.ZeroOrOne
};

var rootCommand = new RootCommand("ASDV - Your AI Coding Assistant")
{
    repoOption,
    providerOption,
    modelOption,
    autoApproveOption,
    sessionOption,
    sessionIdOption,
    maxIterationsOption,
    debugOption,
    onceOption,
    configOption,
    resumeModeOption,
    promptArgument
};

rootCommand.SetHandler(async (context) =>
{
    var repo = context.ParseResult.GetValueForOption(repoOption)!;
    var provider = context.ParseResult.GetValueForOption(providerOption);
    var model = context.ParseResult.GetValueForOption(modelOption);
    var autoApprove = context.ParseResult.GetValueForOption(autoApproveOption);
    var session = context.ParseResult.GetValueForOption(sessionOption);
    var sessionId = context.ParseResult.GetValueForOption(sessionIdOption);
    var maxIterations = context.ParseResult.GetValueForOption(maxIterationsOption);
    var debug = context.ParseResult.GetValueForOption(debugOption);
    var once = context.ParseResult.GetValueForOption(onceOption);
    var config = context.ParseResult.GetValueForOption(configOption);
    var resumeMode = context.ParseResult.GetValueForOption(resumeModeOption)!;
    var prompt = context.ParseResult.GetValueForArgument(promptArgument);

    var ct = context.GetCancellationToken();

    await RunAgentAsync(
        repo,
        provider,
        model,
        autoApprove,
        session,
        sessionId,
        maxIterations,
        debug,
        once,
        config,
        resumeMode,
        prompt,
        ct);
});

return await rootCommand.InvokeAsync(args);

static async Task RunAgentAsync(
    string repo,
    string? provider,
    string? model,
    bool autoApprove,
    string? session,
    string? sessionId,
    int maxIterations,
    bool debug,
    bool once,
    string? config,
    string resumeMode,
    string? prompt,
    CancellationToken ct)
{
    var repoRoot = Path.GetFullPath(repo);

    if (!Directory.Exists(repoRoot))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: Repository path does not exist: {repoRoot}");
        Console.ResetColor();
        return;
    }

    AppConfig? appConfig;
    try
    {
        appConfig = LoadAppConfig(repoRoot, config, debug);
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {ex.Message}");
        Console.ResetColor();
        return;
    }

    string resolvedProvider;
    string resolvedModel;
    try
    {
        resolvedProvider = ResolveProvider(provider, appConfig);
        resolvedModel = ResolveModel(resolvedProvider, model, appConfig);
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {ex.Message}");
        Console.ResetColor();
        return;
    }

    // Create provider
    IModelProvider modelProvider;
    try
    {
        modelProvider = CreateProvider(resolvedProvider, appConfig);
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {ex.Message}");
        Console.ResetColor();
        return;
    }

    // Initialize provider capabilities
    switch (modelProvider)
    {
        case Agent.Llm.OpenAI.OpenAIProvider openai:
            openai.SetModelCapabilities(resolvedModel);
            break;
        case Agent.Llm.Anthropic.ClaudeProvider claude:
            claude.SetModelCapabilities(resolvedModel);
            break;
    }

    // Create workspace
    var workspace = new LocalWorkspace(repoRoot);

    // Create tool registry
    var toolRegistry = CreateToolRegistry();

    // Create services
    var approvalService = new ConsoleApprovalService();
    var policyEngine = new DefaultPolicyEngine(new PolicyOptions { AutoApprove = autoApprove });

    if (once && string.IsNullOrWhiteSpace(prompt))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Error: Prompt is required in --once mode.");
        Console.ResetColor();
        return;
    }

    if (!string.IsNullOrWhiteSpace(session) && !string.IsNullOrWhiteSpace(sessionId))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Error: Use either --session or --session-id, not both.");
        Console.ResetColor();
        return;
    }

    sessionId ??= GenerateSessionId();

    // Create session logger
    var sessionPath = session ?? Path.Combine(
        repoRoot, ".agent", $"session_{sessionId}.jsonl");

    var resumed = File.Exists(sessionPath);
    var messages = new List<Agent.Core.Messages.ChatMessage>();
    var resumedNotes = new Dictionary<string, string>();
    if (resumed)
    {
        var (parsedResumeMode, lastN) = ParseResumeMode(resumeMode);
        var snapshot = SessionLogReader.LoadSession(sessionPath, parsedResumeMode, lastN, warning =>
        {
            if (debug)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Warning: {warning}");
                Console.ResetColor();
            }
        });
        messages = snapshot.Messages;
        resumedNotes = snapshot.Notes;

        if (parsedResumeMode != ResumeMode.Full)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  Resume mode: {resumeMode} ({messages.Count} messages loaded)");
            Console.ResetColor();
        }
    }

    ISessionLogger logger;
    try
    {
        logger = new JsonlSessionLogger(sessionPath);
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Warning: Could not create session log: {ex.Message}");
        Console.ResetColor();
        logger = NullSessionLogger.Instance;
        sessionPath = "(none)";
    }

    // Create session state
    var maxContextTokens = appConfig?.MaxContextTokens
        ?? modelProvider.Capabilities?.MaxContextTokens;

    var sessionState = new SessionState
    {
        SessionId = sessionId,
        ProviderName = resolvedProvider,
        ModelName = resolvedModel,
        MaxContextTokens = maxContextTokens,
        StartedAt = DateTimeOffset.UtcNow
    };

    // Restore work notes from previous session
    foreach (var (key, value) in resumedNotes)
    {
        sessionState.Notes[key] = value;
    }

    // Create options
    var options = new AgentOptions
    {
        RepoRoot = repoRoot,
        Model = resolvedModel,
        Workspace = workspace,
        MaxIterations = maxIterations,
        MaxTokens = 4096,
        SystemPrompt = GetSystemPrompt(toolRegistry, repoRoot, sessionState.Notes),
        State = sessionState,
        IsResumed = resumed
    };

    // Print header
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
    Console.WriteLine("║              ASDV - Agile Synthetic Dev Vibe             ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine($"  Repository: {repoRoot}");
    Console.WriteLine($"  Provider:   {resolvedProvider}");
    Console.WriteLine($"  Model:      {resolvedModel}");
    Console.WriteLine($"  Session:    {sessionPath}");
    Console.WriteLine($"  Session ID: {sessionId}");
    Console.WriteLine($"  Mode:       {(once ? "Once" : "REPL")}");
    Console.WriteLine($"  Auto-approve: {autoApprove}");
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("─────────────────────────────────────────────────────────────");
    Console.ResetColor();
    Console.WriteLine();

    // Create orchestrator
    var orchestrator = new AgentOrchestrator(
        modelProvider,
        toolRegistry,
        approvalService,
        policyEngine,
        logger,
        options);

    try
    {
        await logger.LogAsync(new
        {
            type = "session_start",
            sessionId,
            sessionPath,
            repoRoot,
            provider = resolvedProvider,
            model = resolvedModel,
            mode = once ? "once" : "repl",
            resumed
        });

        await AppendSessionIndexAsync(repoRoot, new
        {
            type = "session",
            sessionId,
            action = resumed ? "resumed" : "created",
            sessionPath,
            repoRoot,
            provider = resolvedProvider,
            model = resolvedModel,
            mode = once ? "once" : "repl"
        });

        var renderer = new ConsoleEventRenderer();

        // Set up command system
        var commandRegistry = new CommandRegistry();
        var commandContext = new CommandContext
        {
            ProviderName = resolvedProvider,
            ModelName = resolvedModel,
            SessionId = sessionId,
            SessionPath = sessionPath,
            RepoRoot = repoRoot,
            AutoApprove = autoApprove,
            State = sessionState
        };
        commandRegistry.Register(new StatusCommand());
        commandRegistry.Register(new DiffCommand());
        commandRegistry.Register(new NotesCommand());
        // HelpCommand needs registry reference — register last
        commandRegistry.Register(new HelpCommand(commandRegistry));

        if (once)
        {
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                await RunStreamToConsoleAsync(orchestrator, renderer, prompt, messages, ct);
            }

            return;
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("REPL mode: type a prompt and press Enter. Use /exit to quit.");
        Console.ResetColor();

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            await RunStreamToConsoleAsync(orchestrator, renderer, prompt, messages, ct);
        }

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("> ");
            Console.ResetColor();
            var input = Console.ReadLine();

            if (input == null)
            {
                break;
            }

            if (string.Equals(input, "/exit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(input, "/q", StringComparison.OrdinalIgnoreCase)
                || string.Equals(input, "/quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            if (input.StartsWith("/"))
            {
                if (commandRegistry.TryExecute(input, commandContext, out var cmdTask))
                {
                    await cmdTask;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Unknown command: {input}. Type /help for available commands.");
                    Console.ResetColor();
                }
                continue;
            }

            await RunStreamToConsoleAsync(orchestrator, renderer, input, messages, ct);
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[Cancelled]");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Red;

        if (debug)
        {
            Console.WriteLine($"[Error] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Stack trace:");
            Console.WriteLine(ex.StackTrace);
        }
        else
        {
            Console.WriteLine($"[Error] {ex.Message}");
            Console.WriteLine("(Run with --debug for detailed error information)");
        }

        Console.ResetColor();
    }
    finally
    {
        await logger.DisposeAsync();
    }
}

static IModelProvider CreateProvider(string provider, AppConfig? appConfig)
{
    var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    return provider.ToLowerInvariant() switch
    {
        "anthropic" => new ClaudeProvider(
            httpClient,
            Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                ?? throw new InvalidOperationException(
                    "ANTHROPIC_API_KEY environment variable is not set")),

        "openai" => CreateOpenAIProvider(
            httpClient,
            Environment.GetEnvironmentVariable("OPENAI_BASE_URL"),
            requireApiKey: true),

        "openai-compatible" => CreateOpenAIProvider(
            httpClient,
            GetRequiredOpenAICompatibleEndpoint(appConfig),
            requireApiKey: false),

        _ => throw new ArgumentException(
            $"Unknown provider: {provider}. Use 'openai', 'anthropic', or 'openai-compatible'.")
    };
}

static OpenAIProvider CreateOpenAIProvider(
    HttpClient httpClient,
    string? baseUrl,
    bool requireApiKey)
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    if (requireApiKey && string.IsNullOrWhiteSpace(apiKey))
    {
        throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set");
    }

    return new OpenAIProvider(httpClient, apiKey, baseUrl);
}

static string ResolveProvider(string? provider, AppConfig? appConfig)
{
    var resolved = !string.IsNullOrWhiteSpace(provider)
        ? provider
        : appConfig?.Provider;

    return string.IsNullOrWhiteSpace(resolved) ? "openai" : resolved.Trim().ToLowerInvariant();
}

static string ResolveModel(string provider, string? model, AppConfig? appConfig)
{
    if (!string.IsNullOrWhiteSpace(model))
    {
        return model.Trim();
    }

    if (!string.IsNullOrWhiteSpace(appConfig?.Model))
    {
        return appConfig!.Model!.Trim();
    }

    return provider switch
    {
        "anthropic" => "claude-sonnet-4-20250514",
        "openai" => "gpt-5.4-mini",
        "openai-compatible" => throw new ArgumentException(
            "Model is required for openai-compatible provider. Set --model or model in asdv.yaml."),
        _ => throw new ArgumentException(
            $"Unknown provider: {provider}. Use 'openai', 'anthropic', or 'openai-compatible'.")
    };
}

static string GetRequiredOpenAICompatibleEndpoint(AppConfig? appConfig)
{
    var endpoint = appConfig?.OpenAICompatibleEndpoint;
    if (string.IsNullOrWhiteSpace(endpoint))
    {
        throw new ArgumentException(
            "OpenAI-compatible endpoint is required. Set openaiCompatibleEndpoint in asdv.yaml.");
    }

    return endpoint.Trim();
}

static AppConfig? LoadAppConfig(string repoRoot, string? configPath, bool debug)
{
    var path = string.IsNullOrWhiteSpace(configPath)
        ? Path.Combine(repoRoot, "asdv.yaml")
        : Path.GetFullPath(configPath);

    try
    {
        return AppConfigLoader.LoadIfExists(path);
    }
    catch (Exception ex)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            throw;
        }

        if (debug)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Warning: Could not load config: {ex.Message}");
            Console.ResetColor();
        }

        return null;
    }
}

static ToolRegistry CreateToolRegistry()
{
    var registry = new ToolRegistry();

    // Read/explore tools
    registry.Register(new ReadFileTool());
    registry.Register(new ListFilesTool());
    registry.Register(new SearchTextTool());

    // Git tools
    registry.Register(new GitStatusTool());
    registry.Register(new GitDiffTool());

    // Write/execute tools
    registry.Register(new FileEditTool());
    registry.Register(new ApplyPatchTool());
    registry.Register(new RunCommandTool());

    // Agent internal tools
    registry.Register(new WorkNotesTool());

    return registry;
}

static string GetSystemPrompt(ToolRegistry toolRegistry, string repoRoot, Dictionary<string, string>? notes = null)
{
    var toolDescriptions = toolRegistry.GetToolDescriptionsMarkdown();

    // Load optional project-level prompt customization
    var projectPromptPath = Path.Combine(repoRoot, ".asdv", "prompt.md");
    var projectPrompt = File.Exists(projectPromptPath)
        ? Environment.NewLine + File.ReadAllText(projectPromptPath)
        : "";

    // Build work notes section
    var notesSection = "";
    if (notes != null && notes.Count > 0)
    {
        var notesText = string.Join(Environment.NewLine, notes.Select(kv => $"  {kv.Key}: {kv.Value}"));
        notesSection = $"""

        ## Current Work Notes

        {notesText}

        These notes persist across turns. Use the WorkNotes tool to update them as you make progress.
        """;
    }

    return $"""
        You are a coding assistant that helps developers with tasks in their local repository.

        ## Available Tools

        {toolDescriptions}
        ## Guidelines

        1. **Understand First**: Always read relevant files before making changes
        2. **Search Effectively**: Use SearchText to locate code patterns
        3. **Precise Edits**: Use FileEdit for targeted string replacements, or ApplyPatch for larger changes
        4. **Verify Results**: Check git status/diff after modifications
        5. **Test Changes**: Run tests when appropriate
        6. **Explain Actions**: Briefly describe what you're doing and why
        7. **Track Progress**: Use WorkNotes to store plans, key findings, and TODOs

        ## Edit Strategies

        For small, targeted changes, prefer FileEdit (exact string replacement).
        For larger or multi-site changes, use ApplyPatch with unified diff format:
        ```
        --- a/path/to/file.cs
        +++ b/path/to/file.cs
        @@ -10,3 +10,4 @@
         context line
        -removed line
        +added line
         context line
        ```

        Keep changes minimal and focused on the specific task.
        {projectPrompt}{notesSection}
        """;
}

static async Task RunStreamToConsoleAsync(
    AgentOrchestrator orchestrator,
    ConsoleEventRenderer renderer,
    string prompt,
    List<Agent.Core.Messages.ChatMessage> messages,
    CancellationToken ct)
{
    await foreach (var evt in orchestrator.RunStreamAsync(prompt, messages, ct))
    {
        renderer.Render(evt);
    }
}

static (ResumeMode mode, int lastN) ParseResumeMode(string input)
{
    if (string.Equals(input, "full", StringComparison.OrdinalIgnoreCase))
        return (ResumeMode.Full, 0);

    if (string.Equals(input, "summary", StringComparison.OrdinalIgnoreCase))
        return (ResumeMode.Summary, 0);

    if (input.StartsWith("last-", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(input[5..], out var n) && n > 0)
        return (ResumeMode.LastN, n);

    return (ResumeMode.Full, 0);
}

static string GenerateSessionId()
{
    var suffix = Guid.NewGuid().ToString("N")[..8];
    return $"{DateTime.UtcNow:yyyyMMddHHmmss}_{suffix}";
}

static async Task AppendSessionIndexAsync(string repoRoot, object entry)
{
    var indexPath = Path.Combine(repoRoot, ".agent", "sessions.jsonl");
    var dir = Path.GetDirectoryName(indexPath);
    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
    {
        Directory.CreateDirectory(dir);
    }

    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    var line = JsonSerializer.Serialize(new
    {
        timestamp = DateTimeOffset.UtcNow,
        data = entry
    }, options);

    await File.AppendAllTextAsync(indexPath, line + Environment.NewLine);
}
