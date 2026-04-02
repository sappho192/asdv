using Agent.Core.Modes;

namespace Agent.Cli.Commands;

public class ModeCommand : ICommand
{
    private readonly ExecutionModeRegistry _modeRegistry;

    public ModeCommand(ExecutionModeRegistry modeRegistry)
    {
        _modeRegistry = modeRegistry;
    }

    public string Name => "mode";
    public string Description => "Switch execution mode: /mode <plan|review|implement|verify|off>";

    public Task ExecuteAsync(string[] args, CommandContext context)
    {
        if (args.Length < 1)
        {
            var currentMode = context.State?.CurrentModeName ?? "(none)";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Current mode: {currentMode}");
            Console.WriteLine($"Available modes: {string.Join(", ", _modeRegistry.GetModeNames())}, off");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        var modeName = args[0].Trim().ToLowerInvariant();

        if (modeName == "off" || modeName == "none")
        {
            context.OnModeChanged?.Invoke(null);
            if (context.State != null)
                context.State.CurrentModeName = null;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Mode disabled. All tools available.");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        var mode = _modeRegistry.GetMode(modeName);
        if (mode == null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Unknown mode: {modeName}");
            Console.WriteLine($"Available modes: {string.Join(", ", _modeRegistry.GetModeNames())}, off");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        context.OnModeChanged?.Invoke(mode);
        if (context.State != null)
            context.State.CurrentModeName = mode.Name;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Mode switched to: {mode.Name}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("(Change takes effect on next prompt)");
        Console.ResetColor();

        return Task.CompletedTask;
    }
}
