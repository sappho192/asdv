using Agent.Core.Events;
using Agent.Core.Orchestrator;
using Agent.Server.Models;
using CoreEvents = Agent.Core.Events;

namespace Agent.Server.Services;

public sealed class SessionRunner
{
    public async Task RunAsync(SessionRuntime session, string userPrompt, CancellationToken ct)
    {
        await session.RunLock.WaitAsync(ct);
        try
        {
            var orchestrator = new AgentOrchestrator(
                session.Provider,
                session.ToolRegistry,
                session.ApprovalService,
                session.PolicyEngine,
                session.Logger,
                session.Options);

            var events = session.Events.Writer;
            session.ApprovalService.AttachChannel(events);

            await foreach (var evt in orchestrator.RunStreamAsync(userPrompt, session.Messages, ct))
            {
                var serverEvent = MapToServerEvent(evt);
                if (serverEvent != null)
                {
                    events.TryWrite(serverEvent);
                }
            }
        }
        finally
        {
            session.RunLock.Release();
        }
    }

    private static ServerEvent? MapToServerEvent(AgentEvent evt) => evt switch
    {
        CoreEvents.TextDelta d => new TextDeltaEvent(d.Text),
        CoreEvents.ToolCallReady r => new ToolCallEvent(r.CallId, r.ToolName, r.ArgsJson),
        CoreEvents.ToolExecutionCompleted c => new Models.ToolResultEvent(c.CallId, c.ToolName, c.Result),
        // ApprovalRequested is not mapped here — ServerApprovalService already writes
        // ApprovalRequiredEvent directly to the channel when approval is requested internally.
        CoreEvents.TraceEvent t when t.Kind == "error" => new Models.TraceEvent(t.Kind, t.Data),
        CoreEvents.AgentCompleted c => new CompletedEvent(c.Reason),
        CoreEvents.AgentError e => new ErrorEvent(e.Message),
        CoreEvents.MaxIterationsReached _ => new ErrorEvent("Max iterations reached."),
        CoreEvents.SessionStarted s => new SessionStartedEvent(s.SessionId, s.Provider, s.Model, s.Resumed),
        CoreEvents.SessionCompleted sc => new SessionCompletedEvent(sc.SessionId, sc.Reason, sc.TotalIterations),
        CoreEvents.SessionError se => new SessionErrorEvent(se.SessionId, se.Message),
        _ => null
    };
}
