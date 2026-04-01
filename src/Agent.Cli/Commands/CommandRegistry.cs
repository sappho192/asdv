namespace Agent.Cli.Commands;

public class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ICommand command)
    {
        _commands[command.Name] = command;
    }

    public bool TryExecute(string input, CommandContext context, out Task task)
    {
        var parts = input.TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            task = Task.CompletedTask;
            return false;
        }

        if (_commands.TryGetValue(parts[0], out var command))
        {
            var args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();
            task = command.ExecuteAsync(args, context);
            return true;
        }

        task = Task.CompletedTask;
        return false;
    }

    public IEnumerable<ICommand> GetAll() => _commands.Values;
}
