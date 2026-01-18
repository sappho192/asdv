using System.CommandLine;
using System.Text.Json;
using Agent.Cli;
using Agent.Core.Approval;
using Agent.Core.Logging;
using Agent.Core.Orchestrator;
using Agent.Core.Policy;
using Agent.Core.Providers;
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

var providerOption = new Option<string>(
    aliases: ["--provider", "-p"],
    getDefaultValue: () => "openai",
    description: "LLM provider (openai|anthropic)");

var modelOption = new Option<string?>(
    aliases: ["--model", "-m"],
    description: "Model name (default: provider-specific)");

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

var promptArgument = new Argument<string?>(
    name: "prompt",
    description: "Task prompt for the agent")
{
    Arity = ArgumentArity.ZeroOrOne
};

var rootCommand = new RootCommand("Local coding agent - Claude Code style")
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
    promptArgument
};

rootCommand.SetHandler(async (context) =>
{
    var repo = context.ParseResult.GetValueForOption(repoOption)!;
    var provider = context.ParseResult.GetValueForOption(providerOption)!;
    var model = context.ParseResult.GetValueForOption(modelOption);
    var autoApprove = context.ParseResult.GetValueForOption(autoApproveOption);
    var session = context.ParseResult.GetValueForOption(sessionOption);
    var sessionId = context.ParseResult.GetValueForOption(sessionIdOption);
    var maxIterations = context.ParseResult.GetValueForOption(maxIterationsOption);
    var debug = context.ParseResult.GetValueForOption(debugOption);
    var once = context.ParseResult.GetValueForOption(onceOption);
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
        prompt,
        ct);
});

return await rootCommand.InvokeAsync(args);

static async Task RunAgentAsync(
    string repo,
    string provider,
    string? model,
    bool autoApprove,
    string? session,
    string? sessionId,
    int maxIterations,
    bool debug,
    bool once,
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

    // Determine model
    model ??= provider.ToLowerInvariant() switch
    {
        "anthropic" => "claude-sonnet-4-20250514",
        "openai" => "gpt-5-mini",
        _ => throw new ArgumentException($"Unknown provider: {provider}")
    };

    // Create provider
    IModelProvider modelProvider;
    try
    {
        modelProvider = CreateProvider(provider, model);
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {ex.Message}");
        Console.ResetColor();
        return;
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
    if (resumed)
    {
        messages = SessionLogReader.LoadMessages(sessionPath, warning =>
        {
            if (debug)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Warning: {warning}");
                Console.ResetColor();
            }
        });
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

    // Create options
    var options = new AgentOptions
    {
        RepoRoot = repoRoot,
        Model = model,
        Workspace = workspace,
        MaxIterations = maxIterations,
        MaxTokens = 4096,
        SystemPrompt = GetSystemPrompt()
    };

    // Print header
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
    Console.WriteLine("║                    Local Coding Agent                     ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine($"  Repository: {repoRoot}");
    Console.WriteLine($"  Provider:   {provider}");
    Console.WriteLine($"  Model:      {model}");
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
            provider,
            model,
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
            provider,
            model,
            mode = once ? "once" : "repl"
        });

        if (once)
        {
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                await orchestrator.RunAsync(prompt, messages, ct);
            }

            return;
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("REPL mode: type a prompt and press Enter. Use /exit to quit.");
        Console.ResetColor();

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            await orchestrator.RunAsync(prompt, messages, ct);
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
                || string.Equals(input, "/quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.Equals(input, "/help", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Commands: /exit, /quit, /help");
                continue;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            await orchestrator.RunAsync(input, messages, ct);
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

static IModelProvider CreateProvider(string provider, string model)
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

        "openai" => new OpenAIProvider(
            httpClient,
            Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? throw new InvalidOperationException(
                    "OPENAI_API_KEY environment variable is not set"),
            Environment.GetEnvironmentVariable("OPENAI_BASE_URL")),

        _ => throw new ArgumentException($"Unknown provider: {provider}. Use 'openai' or 'anthropic'.")
    };
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
    registry.Register(new ApplyPatchTool());
    registry.Register(new RunCommandTool());

    return registry;
}

static string GetSystemPrompt()
{
    return """
        You are a coding assistant that helps developers with tasks in their local repository.

        ## Available Tools

        ### Reading & Exploration
        - **ReadFile**: Read file contents (supports line ranges)
        - **ListFiles**: List files matching glob patterns (e.g., **/*.cs)
        - **SearchText**: Search for text patterns using regex

        ### Git Operations
        - **GitStatus**: Get current repository status
        - **GitDiff**: View changes (staged or unstaged)

        ### Modifications
        - **ApplyPatch**: Apply unified diff patches to modify files
        - **RunCommand**: Execute shell commands (requires approval)

        ## Guidelines

        1. **Understand First**: Always read relevant files before making changes
        2. **Search Effectively**: Use SearchText to locate code patterns
        3. **Precise Changes**: Generate minimal, focused unified diff patches
        4. **Verify Results**: Check git status/diff after modifications
        5. **Test Changes**: Run tests when appropriate
        6. **Explain Actions**: Briefly describe what you're doing and why

        ## Patch Format

        When modifying files, use unified diff format:
        ```
        --- a/path/to/file.cs
        +++ b/path/to/file.cs
        @@ -10,3 +10,4 @@
         context line
        -removed line
        +added line
         context line
        ```

        Keep patches minimal and focused on the specific change needed.
        """;
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
