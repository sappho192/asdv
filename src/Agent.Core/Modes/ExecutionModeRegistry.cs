namespace Agent.Core.Modes;

public sealed class ExecutionModeRegistry
{
    private readonly Dictionary<string, IExecutionMode> _modes = new(StringComparer.OrdinalIgnoreCase);

    public ExecutionModeRegistry()
    {
        Register(new PlanMode());
        Register(new ReviewMode());
        Register(new ImplementMode());
        Register(new VerifyMode());
    }

    public void Register(IExecutionMode mode)
    {
        _modes[mode.Name] = mode;
    }

    public IExecutionMode? GetMode(string name)
    {
        return _modes.TryGetValue(name, out var mode) ? mode : null;
    }

    public IEnumerable<IExecutionMode> GetAllModes() => _modes.Values;

    public IReadOnlyList<string> GetModeNames() => _modes.Keys.ToList();
}
