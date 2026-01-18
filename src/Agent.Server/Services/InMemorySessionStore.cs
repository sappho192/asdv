using System.Collections.Concurrent;
using Agent.Server.Models;

namespace Agent.Server.Services;

public interface ISessionStore
{
    SessionRuntime Create(CreateSessionRequest request);
    bool TryAdd(SessionRuntime session);
    bool TryGet(string id, out SessionRuntime session);
}

public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionRuntime> _sessions = new();
    private readonly SessionRuntimeFactory _factory;

    public InMemorySessionStore(SessionRuntimeFactory factory)
    {
        _factory = factory;
    }

    public SessionRuntime Create(CreateSessionRequest request)
    {
        var session = _factory.Create(request);
        _sessions[session.Info.Id] = session;
        return session;
    }

    public bool TryAdd(SessionRuntime session)
    {
        return _sessions.TryAdd(session.Info.Id, session);
    }

    public bool TryGet(string id, out SessionRuntime session)
    {
        if (_sessions.TryGetValue(id, out var found))
        {
            session = found;
            return true;
        }

        session = null!;
        return false;
    }
}
