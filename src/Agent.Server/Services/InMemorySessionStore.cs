using System.Collections.Concurrent;
using Agent.Server.Models;

namespace Agent.Server.Services;

public interface ISessionStore
{
    SessionInfo Create(CreateSessionRequest request);
    bool TryGet(string id, out SessionInfo session);
}

public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();

    public SessionInfo Create(CreateSessionRequest request)
    {
        var id = Guid.NewGuid().ToString("n");
        var session = new SessionInfo(
            id,
            request.WorkspacePath,
            request.Provider,
            request.Model,
            DateTimeOffset.UtcNow);

        _sessions[id] = session;
        return session;
    }

    public bool TryGet(string id, out SessionInfo session)
    {
        return _sessions.TryGetValue(id, out session);
    }
}
