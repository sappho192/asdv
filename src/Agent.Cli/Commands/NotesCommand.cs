namespace Agent.Cli.Commands;

public class NotesCommand : ICommand
{
    public string Name => "notes";
    public string Description => "Manage work notes: /notes list, /notes set <key> <value>, /notes get <key>, /notes clear [key]";

    public Task ExecuteAsync(string[] args, CommandContext context)
    {
        var notes = context.State?.Notes;
        if (notes == null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Work notes are not available.");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (action)
        {
            case "list":
                if (notes.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("No work notes.");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Work Notes ({notes.Count}):");
                    Console.ResetColor();
                    foreach (var (key, value) in notes)
                    {
                        var preview = value.Length > 80 ? value[..80] + "..." : value;
                        Console.WriteLine($"  {key}: {preview}");
                    }
                }
                break;

            case "set":
                if (args.Length < 3)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Usage: /notes set <key> <value>");
                    Console.ResetColor();
                }
                else
                {
                    var key = args[1];
                    var value = string.Join(" ", args[2..]);
                    notes[key] = value;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Note '{key}' set.");
                    Console.ResetColor();
                }
                break;

            case "get":
                if (args.Length < 2)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Usage: /notes get <key>");
                    Console.ResetColor();
                }
                else if (notes.TryGetValue(args[1], out var val))
                {
                    Console.WriteLine($"{args[1]}: {val}");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Note '{args[1]}' not found.");
                    Console.ResetColor();
                }
                break;

            case "clear":
                if (args.Length < 2)
                {
                    var count = notes.Count;
                    notes.Clear();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Cleared {count} notes.");
                    Console.ResetColor();
                }
                else
                {
                    if (notes.Remove(args[1]))
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"Note '{args[1]}' removed.");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Note '{args[1]}' not found.");
                    }
                    Console.ResetColor();
                }
                break;

            default:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Usage: /notes [list|set|get|clear]");
                Console.ResetColor();
                break;
        }

        return Task.CompletedTask;
    }
}
