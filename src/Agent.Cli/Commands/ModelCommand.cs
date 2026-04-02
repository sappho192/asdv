namespace Agent.Cli.Commands;

public class ModelCommand : ICommand
{
    public string Name => "model";
    public string Description => "Switch model within the same provider: /model <name>";

    public Task ExecuteAsync(string[] args, CommandContext context)
    {
        if (args.Length < 1)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Current model: {context.State?.ModelName ?? context.ModelName}");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        var newModel = args[0].Trim();
        if (string.IsNullOrWhiteSpace(newModel))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Usage: /model <name>");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        if (context.State != null)
        {
            context.State.ModelName = newModel;
        }

        context.OnModelChanged?.Invoke(newModel);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Model switched to: {newModel}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("(Change takes effect on next prompt)");
        Console.ResetColor();

        return Task.CompletedTask;
    }
}
