namespace Agent.Core.Logging;

public interface ISessionLogger : IAsyncDisposable
{
    Task LogAsync<T>(T entry);
}
