using System.Threading.Channels;
using Agent.Core.Logging;
using Agent.Core.Messages;
using Agent.Core.Orchestrator;
using Agent.Core.Policy;
using Agent.Core.Providers;
using Agent.Core.Tools;
using Agent.Server.Models;

namespace Agent.Server.Services;

public sealed class SessionRuntime
{
    private readonly object _streamLock = new();

    public SessionRuntime(
        SessionInfo info,
        AgentOptions options,
        ToolRegistry toolRegistry,
        IModelProvider provider,
        IPolicyEngine policyEngine,
        ISessionLogger logger,
        ServerApprovalService approvalService)
    {
        Info = info;
        Options = options;
        ToolRegistry = toolRegistry;
        Provider = provider;
        PolicyEngine = policyEngine;
        Logger = logger;
        ApprovalService = approvalService;
    }

    public SessionInfo Info { get; }
    public AgentOptions Options { get; }
    public ToolRegistry ToolRegistry { get; }
    public IModelProvider Provider { get; }
    public IPolicyEngine PolicyEngine { get; }
    public ISessionLogger Logger { get; }
    public ServerApprovalService ApprovalService { get; }
    public List<ChatMessage> Messages { get; } = new();
    public Channel<ServerEvent> Events { get; } = Channel.CreateUnbounded<ServerEvent>();
    public SemaphoreSlim RunLock { get; } = new(1, 1);
    public bool StreamConnected { get; private set; }

    public bool TryOpenStream()
    {
        lock (_streamLock)
        {
            if (StreamConnected)
            {
                return false;
            }

            StreamConnected = true;
            return true;
        }
    }

    public void CloseStream()
    {
        lock (_streamLock)
        {
            StreamConnected = false;
        }
    }
}
