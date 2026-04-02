namespace Agent.Cli.Commands;

public class StatusCommand : ICommand
{
    public string Name => "status";
    public string Description => "Show current session status";

    public Task ExecuteAsync(string[] args, CommandContext context)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Session Status");
        Console.ResetColor();
        Console.WriteLine($"  Provider:     {context.State?.ProviderName ?? context.ProviderName}");
        Console.WriteLine($"  Model:        {context.State?.ModelName ?? context.ModelName}");
        Console.WriteLine($"  Session ID:   {context.SessionId}");
        Console.WriteLine($"  Session path: {context.SessionPath}");
        Console.WriteLine($"  Repository:   {context.RepoRoot}");
        Console.WriteLine($"  Auto-approve: {context.GetLiveAutoApprove()}");

        var state = context.State;
        if (state != null)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Runtime State");
            Console.ResetColor();
            Console.WriteLine($"  Iterations:   {state.IterationCount}");
            Console.WriteLine($"  Messages:     {state.MessageCount}");
            Console.WriteLine($"  Last tool:    {state.LastToolName ?? "(none)"}");

            var totalTokens = state.EstimatedInputTokens + state.EstimatedOutputTokens;
            if (state.MaxContextTokens.HasValue && state.MaxContextTokens > 0)
            {
                var pct = (double)state.EstimatedInputTokens / state.MaxContextTokens.Value * 100;
                Console.WriteLine($"  Tokens:       ~{FormatTokenCount(state.EstimatedInputTokens)} / {FormatTokenCount(state.MaxContextTokens.Value)} ({pct:F0}% context)");
            }
            else if (totalTokens > 0)
            {
                Console.WriteLine($"  Tokens:       ~{FormatTokenCount(totalTokens)} (in: {FormatTokenCount(state.EstimatedInputTokens)}, out: {FormatTokenCount(state.EstimatedOutputTokens)})");
            }

            var files = state.RecentFilesTouched.ToArray();
            if (files.Length > 0)
            {
                Console.WriteLine($"  Files touched: {files.Length}");
                foreach (var f in files.Take(10))
                    Console.WriteLine($"    - {f}");
                if (files.Length > 10)
                    Console.WriteLine($"    ... and {files.Length - 10} more");
            }

            if (state.Notes.Count > 0)
            {
                Console.WriteLine($"  Work notes:   {state.Notes.Count}");
            }
        }

        return Task.CompletedTask;
    }

    private static string FormatTokenCount(long tokens)
    {
        return tokens >= 1000 ? $"{tokens / 1000.0:F1}k" : tokens.ToString();
    }
}
