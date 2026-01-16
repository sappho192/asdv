using Agent.Core.Logging;

namespace Agent.Logging;

public class NullSessionLogger : ISessionLogger
{
    public static NullSessionLogger Instance { get; } = new();

    private NullSessionLogger() { }

    public Task LogAsync<T>(T entry) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
