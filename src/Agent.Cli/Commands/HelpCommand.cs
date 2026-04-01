namespace Agent.Cli.Commands;

public class HelpCommand : ICommand
{
    private readonly CommandRegistry _registry;

    public HelpCommand(CommandRegistry registry)
    {
        _registry = registry;
    }

    public string Name => "help";
    public string Description => "Show available commands";

    public Task ExecuteAsync(string[] args, CommandContext context)
    {
        Console.WriteLine("Available commands:");
        foreach (var cmd in _registry.GetAll())
        {
            Console.WriteLine($"  /{cmd.Name,-12} {cmd.Description}");
        }
        Console.WriteLine($"  /{"exit",-12} Exit the REPL");
        return Task.CompletedTask;
    }
}
