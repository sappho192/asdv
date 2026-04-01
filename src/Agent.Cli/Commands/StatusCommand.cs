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
        Console.WriteLine($"  Provider:     {context.ProviderName}");
        Console.WriteLine($"  Model:        {context.ModelName}");
        Console.WriteLine($"  Session ID:   {context.SessionId}");
        Console.WriteLine($"  Session path: {context.SessionPath}");
        Console.WriteLine($"  Repository:   {context.RepoRoot}");
        Console.WriteLine($"  Auto-approve: {context.AutoApprove}");
        return Task.CompletedTask;
    }
}
