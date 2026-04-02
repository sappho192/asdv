namespace Agent.Cli.Commands;

public class ApproveAllCommand : ICommand
{
    public string Name => "approve-all";
    public string Description => "Toggle auto-approve for Medium/Low risk tools (High-risk always requires approval)";

    public Task ExecuteAsync(string[] args, CommandContext context)
    {
        if (context.OnApproveAllToggled == null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Auto-approve toggle is not available.");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        var newState = context.OnApproveAllToggled.Invoke();

        if (newState)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Auto-approve: ON");
            Console.WriteLine("  Medium/Low risk tools will execute without approval.");
            Console.WriteLine("  High-risk tools (e.g., RunCommand) still require approval.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Auto-approve: OFF");
            Console.WriteLine("  All tools requiring approval will prompt as usual.");
            Console.ResetColor();
        }

        return Task.CompletedTask;
    }
}
